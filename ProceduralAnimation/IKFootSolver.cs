using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralAnimation
{

    public class IKFootSolver : MonoBehaviour, IIKFootSolver<bool>
    {
        public LayerMask terrainLayer;
        public Transform body;
        public GameObject foot;
        [SerializeField] private Rigidbody footRb;
        [SerializeField] private IKFootSolver otherFoot;
        [SerializeField] float legLength = 1;
        [SerializeField] private PlayerAnimation playerAnimation;
        [SerializeField] private Vector3 footOffset = default; // this is a pose-based foot desired local offset from a theoretical resting position
        [SerializeField] private Vector3 footRotation = default; // this is a pose-based desired local space foot forward vector 
        [SerializeField] private float footNormalOffset; // this is an offset caused by the ankle joint being offset from the ground a certain amount

        [SerializeField] private float footSpacing;
        [SerializeField] private float minFootSpacing;

        [SerializeField] private float maxFootAngle;
        [SerializeField] private float maxStepHeight;

        [SerializeField] private float minStepLength;
        [SerializeField] private float maxStepLength;


        [SerializeField] private StepParams stepParams;



        [SerializeField] private float lerp = 0;

        float bodyH;
        Vector3 oldPos, curPos, newPos;
        Vector3 oldNormal, curNormal, curForward, newNormal;

        public bool grounded;
        [SerializeField] bool stickToCurPos = true;

        private Vector3 playerPos;
        private Vector3 playerLook;

        [HideInInspector] private Vector3 barrierHit;
        [HideInInspector] private Vector3 barrierTopPos;
        [HideInInspector] private Vector3 desiredPos;
        [HideInInspector] private Vector3 initialPos;

        public void Setup()
        {
            oldPos = curPos = newPos = this.transform.position;

            // Initialize the foot Normal to the transform.up
            oldNormal = curNormal = newNormal = transform.up;
            // Desired distance of the foot from the body center
            // Initialize with default distance, displace according to current pose
            footSpacing = Mathf.Sign(transform.localPosition.x) * 0.12f;
            Ray ray = new Ray(foot.transform.position, Vector3.down);
            Physics.Raycast(ray, out RaycastHit footInfo, 10, terrainLayer.value);
            footNormalOffset = (footInfo.point - foot.transform.position).y;
            this.transform.position = footInfo.point;
            footRotation = body.forward;
            curForward = body.forward;
            if (footRb is null)
                footRb = foot.gameObject.GetComponent<Rigidbody>();
            ray = new Ray(body.position, Vector3.down);
            Physics.Raycast(ray, out RaycastHit bodyInfo, 10, terrainLayer.value);
            if (bodyH < 1e-4)
                bodyH = (body.position - bodyInfo.point).magnitude;
            attempts = 0;
        }

        public bool isMoving()
        {
            return lerp < 1;
        }

        public (bool, RaycastHit) GroundCheck(float radius = 0.25f, float eps = 0.15f, float fallback = 0.25f)
        {
            Ray ray = new Ray(footRb.worldCenterOfMass, -foot.transform.up);

            if (Physics.Raycast(ray, out RaycastHit hitInfo, radius, terrainLayer.value))
            {
                return (true, hitInfo);
            }

            // No luck, but maybe a spherecast will work
            if (fallback > 1e-5f)
            {
                var positions = Physics.SphereCastAll(ray, radius, fallback, terrainLayer.value);

                if (positions.Length == 0)
                    return (false, hitInfo);

                Vector3 targetPos = footRb.worldCenterOfMass;
                Array.Sort(positions, (lhs, rhs) =>
                {
                    float lhsDist = Vector3.Distance(lhs.point, targetPos);
                    float rhsDist = Vector3.Distance(rhs.point, targetPos);
                    return lhsDist < rhsDist ? -1 : Convert.ToInt32(lhsDist > rhsDist);
                });

                return (Vector3.Distance(positions[0].point, targetPos) < eps, positions[0]);

            }
            return (false, hitInfo);
        }

        public void UpdatePose(Vector3 offset, Vector3 lookDir)
        {
            footOffset = offset;
            footRotation = lookDir;
        }

        public int ComparePositions(Vector3 target, RaycastHit lhs, RaycastHit rhs)
        {
            float QuadMean(float a1, float a2, float a3)
            {
                return Mathf.Sqrt((a1 * a1 + a2 * a2 + a3 * a3) / 3);
            }

            float Hypot(float x1, float x2)
            {
                return Mathf.Sqrt(x1 * x1 + x2 * x2);
            }

            float lhsForwardProj = PlayerMotionData.CalculateProjectionLength(lhs.point - target, body.forward);
            float rhsForwardProj = PlayerMotionData.CalculateProjectionLength(rhs.point - target, body.forward);
            float lhsRightProj = PlayerMotionData.CalculateProjectionLength(lhs.point - target, body.right);
            float rhsRightProj = PlayerMotionData.CalculateProjectionLength(rhs.point - target, body.right);
            float lhsHorizontal = Hypot(lhsRightProj, lhsForwardProj);
            float rhsHorizontal = Hypot(rhsRightProj, rhsForwardProj);
            bool horProjL = (footSpacing > 0 ? lhsRightProj >= minFootSpacing : lhsRightProj <= minFootSpacing) && lhsHorizontal < maxStepLength;
            bool horProjR = (footSpacing > 0 ? rhsRightProj >= minFootSpacing : rhsRightProj <= minFootSpacing) && rhsHorizontal < maxStepLength;

            // In short - we prefer to step forward and points which are atleast minFootSpacing to the side of the body center, and dont require to move the leg very high up
            if (horProjL && horProjR)
            {
                if (lhsForwardProj > 0 && rhsForwardProj < 0)
                {
                    // Prefer steping forward => choosing to step to lhs is less effort
                    return -1;
                }
                else if (lhsForwardProj < 0 && rhsForwardProj > 0)
                {
                    return 1;
                }
            }
            else
            {
                if (horProjL && !horProjR)
                    return -1;
                else if (!horProjL && horProjR)
                    return 1;
            }

            // Either both points are backward or both are forward
            // both are also ok to put our foot down at

            float lhsVertProj = Vector3.Project(lhs.point - target, Vector3.up).y;
            float rhsVertProj = Vector3.Project(rhs.point - target, Vector3.up).y;

            // Score both based on how close the horizontal projection is to zero, vertical movement required and X-wise orientation using quad mean
            float lhsScore = QuadMean(Mathf.Abs(lhsHorizontal), Mathf.Abs(lhsRightProj - footSpacing), Mathf.Pow(Mathf.Log(-lhsVertProj / stepParams.StepHeight + 1, 2), 2));
            float rhsScore = QuadMean(Mathf.Abs(rhsHorizontal), Mathf.Abs(rhsRightProj - footSpacing), Mathf.Pow(Mathf.Log(-rhsVertProj / stepParams.StepHeight + 1, 2), 2));
            if (lhsScore != rhsScore)
                return lhsScore < rhsScore ? -1 : 1;
            float lhsDist = Vector3.Distance(lhs.point, target);
            float rhsDist = Vector3.Distance(rhs.point, target);
            if (lhsDist != rhsDist)
                return lhsDist < rhsDist ? -1 : 1;
            return 0;
        }


        private bool VerifyPosition(Vector3 position, Vector3 normal)
        {
            var dir = position - body.position;
            return Vector3.Angle(Vector3.up, normal) < maxFootAngle && Vector3.Distance(body.transform.position, position) <= legLength + 0.1f && Vector3.ProjectOnPlane(dir, normal).magnitude <= maxStepLength;
        }

        [HideInInspector] private List<RaycastHit> localSearchPoints;
        private RaycastHit[] LocalSearch(Vector3 position, float radius, bool sort = true, bool offsetSphereCenter = true)
        {
            RaycastHit[] points = Physics.SphereCastAll(offsetSphereCenter ? position - Vector3.up * radius : position, radius, Vector3.up, maxStepHeight * 1.1f, terrainLayer.value);
            if (sort)
            {
                Array.Sort(points, (lhs, rhs) => { return ComparePositions(position, lhs, rhs); });
            }
            
            localSearchPoints = new List<RaycastHit>(points.Length);
            localSearchPoints.AddRange(points);
            return points;
        }

        // Player might rotate in place or while stepping - in this case lazily update foot rotation
        [SerializeField] float footRotationSLerp;
        public void AdjustFootRotation(bool force = false)
        {
            Vector3 predictedV = playerAnimation.PredictVelocityVector(Time.fixedDeltaTime);
            Vector3 predictedForward = playerAnimation.PredictVectorDir(body.forward, Time.fixedDeltaTime);
            if (playerAnimation.PredictVelocityVector(Time.fixedDeltaTime).magnitude > 0.1f && PlayerMotionData.CalculateProjectionLength(predictedV, body.forward) > 0 && PlayerMotionData.CalculateProjectionLength(predictedV, body.right * Mathf.Sign(footSpacing)) > 0)
            {
                // The predicted velocity is pointing forward and in the correct direction for the leg, so we want to use an intermediate between body.forward and the velocity vector
                footRotationSLerp = Vector3.Dot(predictedForward.normalized, predictedV.normalized);
                Vector3 newForward = Vector3.Slerp(body.TransformDirection(footRotation), predictedV, footRotationSLerp);
                if (Vector3.Angle(curForward, newForward) >= 5 || force)
                {
                    this.transform.forward = newForward;
                    curForward = newForward;
                }
                else
                {
                    this.transform.forward = curForward;
                }
            }
            else
            {
                // Velocity vector is either pointing backwards, or in the inward direction of the foot
                // blend between forward and desired pose rotation based on forward projection magnitude
                footRotationSLerp = Vector3.Project(predictedForward.normalized, body.forward).magnitude;
                Vector3 newForward = Vector3.Slerp(body.TransformDirection(footRotation), body.forward, footRotationSLerp);
                if (Vector3.Angle(curForward, newForward) >= 5 || force)
                {
                    this.transform.forward = newForward;
                    curForward = newForward;
                }
                else
                {
                    this.transform.forward = curForward;
                }
            }

        }

        [SerializeField] public int attempts;

        public void PlantFoot()
        {
            var res = GroundCheck();
            grounded = res.Item1;
            if (grounded)
            {
                stickToCurPos = true;
                attempts = 0;
            }
            else if (attempts < 1)
            {
                Vector3 searchCenter = 0.5f * (body.transform.position + newPos);
                searchCenter.y = body.position.y - bodyH;
                var points = LocalSearch(searchCenter, maxStepLength, true, false);
                for (int i = 0; i < points.Length; ++i)
                {
                    if (Vector3.Distance(points[i].point, this.transform.position) > 0.1f && VerifyPosition(points[i].point, points[i].normal))
                    {
                        attempts += 1;
                        stickToCurPos = true;
                        newPos = points[i].point;
                        newNormal = points[i].normal;
                        lerp -= Vector3.Distance(this.transform.position, newPos) / Vector3.Distance(oldPos, this.transform.position);
                    }
                }
            }
            else
            {
                // for some reason we were unable to place our foot down properly
                // Use "mechanical intelligence"
                stickToCurPos = false;
            }
        }

        // Selects best fit position around a starting point
        public Tuple<Vector3, Vector3> GetBestFitPosition(Vector3 pos, float radius)
        {
            var points = LocalSearch(pos, radius, true, false);
            for (int i = 0; i < points.Length; ++i)
            {
                if (VerifyPosition(points[i].point, points[i].normal))
                {
                    return Tuple.Create(points[i].point, points[i].normal);
                }
            }
            return null;
        }

        void StepIteration()
        {
            if (lerp < 1)
            {
                AdjustFootRotation();
                grounded = false;
                Vector3 tempPosition = Vector3.Lerp(oldPos, newPos, lerp);
                tempPosition.y += Mathf.Sin(lerp * Mathf.PI) * stepParams.StepHeight;
                // Check if there is anything on the path
                Vector3 dirVector = tempPosition - curPos;
                if (Physics.CapsuleCast(curPos, tempPosition, 0.1f, dirVector, out RaycastHit barrierInfo, dirVector.magnitude, terrainLayer.value))
                {
                    // Adjust the new position
                    // First try stepping over the blocking object. If that doesn't work, then try to find best fit position next to it
                    if (Vector3.Distance(barrierInfo.point, newPos) < 0.1f || Vector3.Distance(barrierInfo.point, tempPosition) < 0.1f)
                    {
                        curPos = barrierInfo.point - 0.1f * dirVector;
                        curNormal = Vector3.Lerp(oldNormal, newNormal, lerp);
                        lerp += Time.fixedDeltaTime * stepParams.StepSpeed;
                        return;
                    }
                    barrierHit = barrierInfo.point;
                    //Debug.Log("Something is blocking the path");
                    Vector3 barrierTop = new Vector3(barrierInfo.point.x + dirVector.normalized.x * 0.1f, body.position.y - legLength + maxStepHeight, barrierInfo.point.z + dirVector.normalized.z * 0.1f);
                    if (Physics.Raycast(barrierTop, Vector3.down, out RaycastHit barrierTopInfo, maxStepHeight, terrainLayer.value))
                    {
                        barrierTopPos = barrierTopInfo.point;
                        oldPos = (curPos - barrierTopInfo.point) / (1 - lerp) + barrierTopInfo.point;
                        newPos = barrierTopInfo.point;
                        newNormal = Vector3.up;
                    }

                }
                curPos = tempPosition;
                curNormal = Vector3.Lerp(oldNormal, newNormal, lerp);
                lerp += Time.fixedDeltaTime * stepParams.StepSpeed;
                if (lerp >= 1.0f)
                {
                    stickToCurPos = true;
                    stepParams.TryApplyMutation();
                    PlantFoot();
                    // Update once every time a step finishes
                    playerPos = body.position;
                    playerLook = body.forward;
                }
            }
            else
            {
                var groundCheckRes = GroundCheck();
                grounded = groundCheckRes.Item1;
                if (!grounded)
                {
                    // just use mechanical intelligence to position the leg.
                    stickToCurPos = false;
                }
            }

        }

        // Whether we care about the other foot finishing the step or not is decided by PlayerAnimation class
        public void Step(bool ignoreOtherFoot = false)
        {
            // Start animating with lerp, reset attempt counter etc.
            void InitializeStep(Vector3 position, Vector3 normal)
            {
                lerp = 0;
                attempts = 0;
                newNormal = normal;
                newPos = position;
                oldPos = foot.transform.position;
                oldNormal = foot.transform.up;
            }

            // behaviour for when the player is grounded
            if (playerAnimation.GetGroundedState())
            {
                if (stickToCurPos)
                {
                    transform.position = curPos;
                    transform.up = curNormal;
                    transform.forward = curForward;
                }
                if (lerp >= 1.0f)
                {
                    // Use the fact that the player is grounded as an estimate for y position
                    initialPos = body.position + (body.right * footSpacing) + Vector3.down * bodyH;
                    float dist = Vector3.Distance(initialPos, this.transform.position);
                    if ((dist >= stepParams.StepDistance ||
                        Vector3.Project(body.transform.position - foot.transform.position, body.right * Mathf.Sign(footSpacing)).magnitude <= minFootSpacing) && 
                        (otherFoot.lerp >= 1.0f || ignoreOtherFoot))
                    {
                        stickToCurPos = true;

                        // Always step in the velocity direction if possible
                        desiredPos = initialPos + Vector3.ProjectOnPlane(playerAnimation.GetVelocityVector(), Vector3.up).normalized * stepParams.StepLength + body.TransformDirection(footOffset);
                        Ray ray = new Ray(new Vector3(desiredPos.x, body.transform.position.y, desiredPos.z), Vector3.down);
                        if (Physics.Raycast(ray, out RaycastHit desiredInfo, legLength + 1, terrainLayer.value))
                        {
                            if (VerifyPosition(desiredInfo.point, desiredInfo.normal))
                            {
                                InitializeStep(desiredInfo.point, desiredInfo.normal);
                            }
                        }
                        if (lerp > 0)
                        {
                            var bestFit = GetBestFitPosition(desiredPos, maxStepLength);
                            if (bestFit != null)
                            {
                                InitializeStep(bestFit.Item1, bestFit.Item2);
                            }
                            else
                            {
                                Debug.Log("Best Fit failed verification");
                                bestFit = GetBestFitPosition(body.position + Vector3.down * bodyH, maxStepLength);
                                if (bestFit != null)
                                {
                                    InitializeStep(bestFit.Item1, bestFit.Item2);
                                }
                                else
                                {
                                    Debug.Log("Unable to find valid position for foot placement");
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Force an update if the direction has changed since last step ended
                    if (Vector3.Angle(playerLook, body.forward) >= 15.0f)
                    {
                        AdjustFootRotation(true);
                    }
                }
                StepIteration();
            }
            else
            {
                //curPos = body.position + (body.right * footSpacing) + Vector3.down * bodyH + Vector3.ProjectOnPlane(playerAnimation.GetVelocityVector(), Vector3.up).normalized * stepParams.StepLength + body.TransformDirection(footOffset);
            }

        }

        public Transform Body
        {
            get { return body; }
        }

        public Vector3 DesiredPos
        {
            get { return desiredPos; }
        }

        public List<RaycastHit> LocalSearchPoints
        {
            get { return localSearchPoints; }
        }

        public Vector3 InitialPos
        {
            get { return initialPos; }
        }

        public Vector3 BarrierTopPos
        {
            get { return barrierTopPos; }
        }

        public Vector3 BarrierHit
        {
            get { return barrierHit; }
        }

        public Vector3 LastUpdatedLook
        {
            get { return playerLook; }
        }

        public Vector3 LastUpdatedPosition
        {
            get { return playerPos; }
        }

        public Vector3 EffectorPosition
        {
            get { return lerp >= 1.0f ? newPos : curPos; }
        }

        public Vector3 EffectorNormal
        {
            get { return lerp >= 1.0f ? newNormal : curNormal; }
        }

        public Vector3 TargetPosition
        {
            get { return newPos; }
        }

        public Vector3 TargetNormal
        {
            get { return newNormal; }
        }

        public bool isAnimating()
        {
            return lerp < 1.0f;
        }

        public LayerMask TerrainLayer
        {
            get { return terrainLayer; }
        }

        public void UpdateStepParameters(float newStepLength, float newStepHeight, float newStepDistance, float newStepPeriod, float recalculationLimit = 0.1f)
        {
            // If its a substantial difference - recalculate our step length and shorten the step
            bool flag = stepParams.StepLength - newStepLength > recalculationLimit && (lerp < newStepLength / stepParams.StepLength && lerp < 1.0f);
            stepParams.StepLength = newStepLength;
            stepParams.StepHeight = newStepHeight;
            stepParams.StepDistance = newStepDistance;
            stepParams.StepSpeed = 2 / newStepPeriod;
            if (lerp >= 1.0f || newStepLength - stepParams.StepLength > recalculationLimit)
            {
                stepParams.TryApplyMutation();
                return;
            }
            if (!flag)
                return;
            desiredPos = body.position + (body.right * footSpacing) + Vector3.down * legLength + Vector3.ProjectOnPlane(playerAnimation.GetVelocityVector(), Vector3.up).normalized * newStepLength + body.TransformDirection(footOffset);
            Ray ray = new Ray(new Vector3(desiredPos.x, body.transform.position.y, desiredPos.z), Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit desiredInfo, legLength + 1, terrainLayer.value))
            {
                if (VerifyPosition(desiredInfo.point, desiredInfo.normal))
                {
                    newNormal = desiredInfo.normal;
                    newPos = desiredInfo.point;
                    return;
                }
            }
            // At this point the simplest course of action is to just step to the previously selected position without adjusting 
            return;
        }

    }
}
