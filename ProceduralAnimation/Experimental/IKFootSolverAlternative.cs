using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralAnimation
{

    public class IKFootSolverAlternative : MonoBehaviour, IIKFootSolver<(IKMultiPoseCycler, float, float, bool)>
    {
        public LayerMask terrainLayer;
        public Transform body;
        public GameObject foot;
        [SerializeField] bool doAdjust = true;
        [SerializeField] private Rigidbody footRb;
        [SerializeField] private IKFootSolverAlternative otherFoot;
        [SerializeField] float legLength = 1;
        [SerializeField] private PlayerAnimation playerAnimation;
        [SerializeField] private Vector3 footOffset = default;      // this is a pose-based foot desired local offset from a theoretical resting position
        [SerializeField] private Vector3 footRotation = default;    // this is a pose-based desired local space foot forward vector 
        [SerializeField] private float footNormalOffset;            // this is an offset caused by the ankle joint being offset from the ground a certain amount

        [SerializeField] private float footSpacing;
        [SerializeField] private float minFootSpacing;

        [SerializeField] private float maxFootAngle;
        [SerializeField] private float maxStepHeight;

        [SerializeField] private float minStepLength;
        [SerializeField] private float maxStepLength;
        [SerializeField] private AnimationCurve velocityContribution;


        [SerializeField] private float lerp = 0;

        float bodyH;
        Vector3 oldPos, curPos, newPos;
        Vector3 oldNormal, curNormal, curForward, newNormal;

        public bool grounded;

        private Vector3 playerPos;
        private Vector3 playerLook;

        [HideInInspector] public Vector3 barrierHit { get; private set; }
        [HideInInspector] public Vector3 barrierTopPos { get; private set; }
        [HideInInspector] public Vector3 desiredPos { get; private set; }
        [HideInInspector] public Vector3 initialPos { get; private set; }

        public void Setup()
        {
            oldPos = curPos = newPos = this.transform.position;

            // Initialize the foot Normal to the transform.up
            oldNormal = curNormal = newNormal = transform.up;
            // Desired distance of the foot from the body center
            // Initialize with default distance, displace according to current pose
            footSpacing = Mathf.Sign(transform.localPosition.x) * 0.1f;
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
            float lhsScore = QuadMean(Mathf.Abs(lhsHorizontal), Mathf.Abs(lhsRightProj - footSpacing), Mathf.Pow(Mathf.Log(-lhsVertProj / bodyH + 1, 2), 2));
            float rhsScore = QuadMean(Mathf.Abs(rhsHorizontal), Mathf.Abs(rhsRightProj - footSpacing), Mathf.Pow(Mathf.Log(-rhsVertProj / bodyH + 1, 2), 2));
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

        [HideInInspector] public List<RaycastHit> localSearchPoints;
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
        public Vector3 AdjustFootRotation(bool force = false)
        {
            Vector3 predictedV = playerAnimation.PredictVelocityVector(Time.fixedDeltaTime);
            Vector3 predictedForward = playerAnimation.PredictVectorDir(body.forward, Time.fixedDeltaTime);
            if (playerAnimation.PredictVelocityVector(Time.fixedDeltaTime).magnitude > 0.1f && PlayerMotionData.CalculateProjectionLength(predictedV, body.forward) > 0 && PlayerMotionData.CalculateProjectionLength(predictedV, body.right * Mathf.Sign(footSpacing)) > 0)
            {
                // The predicted velocity is pointing forward and in the correct direction for the leg, so we want to use an intermediate between body.forward and the velocity vector
                footRotationSLerp = Vector3.Dot(predictedForward.normalized, predictedV.normalized);
                Vector3 newForward = Vector3.Slerp(body.TransformDirection(footRotation), predictedV, footRotationSLerp);
                if (Vector3.Angle(curForward, newForward) >= 5.0f || force)
                {
                    return newForward;
                }

                return curForward;

            }
            else
            {
                // Velocity vector is either pointing backwards, or in the inward direction of the foot
                // blend between forward and desired pose rotation based on forward projection magnitude
                footRotationSLerp = Vector3.Project(predictedForward.normalized, body.forward).magnitude;
                Vector3 newForward = Vector3.Slerp(body.TransformDirection(footRotation), body.forward, footRotationSLerp);
                if (Vector3.Angle(curForward, newForward) >= 5.0f || force)
                {
                    return newForward;
                }

                return curForward;
            }

        }

        [SerializeField] public int attempts;

        // Selects best fit position around a starting point (returns position and normal vector)
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

        // Adjusts pose to avoid collision
        private IKPose AdjustTargetPosition(IKPose desiredPose, float extendBudget=0.2f, float sideStepRad=0.2f)
        {
            Vector3 desiredPos = desiredPose.WorldPos;
            Vector3 dirVector = desiredPose.WorldPos - this.transform.position;

            IKPose? GetBestBarrierPos(RaycastHit barrierTopInfo)
            {
                // Sometimes its better to step over a small barrier
                if (desiredPose.AdjustType == IKPose.AdjustmentType.Attract || desiredPose.AdjustType == IKPose.AdjustmentType.Stick)
                {
                    var points = Physics.SphereCastAll(barrierTopInfo.point + 0.1f * barrierTopInfo.normal, sideStepRad, dirVector.normalized, dirVector.magnitude, terrainLayer.value);
                    if (points.Length > 0)
                    {
                        Array.Sort(points, (lhs, rhs) => { return ComparePositions(desiredPos, lhs, rhs); });
                        for (int j = 0; j < points.Length; ++j)
                        {
                            if (VerifyPosition(points[j].point, points[j].normal) && Vector3.Distance(points[j].point, desiredPos) <= extendBudget)
                            {
                                desiredPose.WorldPos = points[j].point + 0.01f * points[j].normal;
                                desiredPose.WorldRot = Quaternion.FromToRotation(Vector3.up, points[j].normal);
                                return desiredPose;
                            }
                        }
                    }
                    // We can still plant our foot 
                    if (VerifyPosition(barrierTopInfo.point, barrierTopInfo.normal))
                    {
                        barrierTopPos = barrierTopInfo.point;
                        desiredPose.WorldPos = barrierTopPos;
                        desiredPose.WorldRot = Quaternion.FromToRotation(Vector3.up, barrierTopInfo.normal);
                        return desiredPose;
                    }
                    return null;

                }

                barrierTopPos = barrierTopInfo.point;
                desiredPose.WorldPos = barrierTopInfo.normal * 0.1f + barrierTopPos;
                desiredPose.WorldRot = Quaternion.FromToRotation(Vector3.up, barrierTopInfo.normal);
                return desiredPose;
            }

            
            if (Physics.CapsuleCast(this.transform.position, desiredPos, 0.1f, dirVector, out RaycastHit barrierInfo, dirVector.magnitude, terrainLayer.value))
            {
                // Adjust the new position
                // Adjust slightly - the position is good enough
                if (Vector3.Distance(barrierInfo.point, desiredPose.Position) < 0.1f)
                {
                    desiredPose.WorldPos = barrierInfo.point - 0.1f * dirVector;
                    return desiredPose;
                }
                // No luck - find the top of the barrier
                barrierHit = barrierInfo.point;
                Vector3 barrierTop = new Vector3(barrierInfo.point.x + dirVector.normalized.x * 0.1f, body.position.y - bodyH + maxStepHeight, barrierInfo.point.z + dirVector.normalized.z * 0.1f);

                if (Physics.Raycast(barrierTop, Vector3.down, out RaycastHit barrierTopInfo, maxStepHeight, terrainLayer.value))
                {
                    var res = GetBestBarrierPos(barrierTopInfo);
                    if (res == null)
                    {
                        desiredPose.WorldPos = barrierInfo.point - 0.1f * dirVector;
                    }
                    return res ?? desiredPose;
                }
                var points = Physics.SphereCastAll(barrierTop, sideStepRad, Vector3.down, maxStepHeight, terrainLayer.value);
                Array.Sort(points, (lhs, rhs) => { return ComparePositions(desiredPos, lhs, rhs); });
                for (int i = 0; i < points.Length; ++i)
                {
                    var res = GetBestBarrierPos(barrierTopInfo);
                    if (res.HasValue)
                        return res.Value;
                }
                desiredPose.WorldPos = barrierInfo.point - 0.1f * dirVector;
                return desiredPose;
            }
            return desiredPose;
        }

        // Adjusts target to be plantet on the ground
        private IKPose? PlantTargetPosition(Vector3 desiredPos, Quaternion rotation, IKPose.AdjustmentType adjType)
        {
            IKPose targetPose = new IKPose(desiredPos, rotation, adjType, false, this.transform);
            Ray ray = new Ray(new Vector3(desiredPos.x, body.position.y, desiredPos.z), Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit desiredInfo, legLength + 0.2f, terrainLayer.value))
            {
                if (VerifyPosition(desiredInfo.point, desiredInfo.normal))
                {
                    targetPose.WorldPos = desiredInfo.point + footOffset;
                    targetPose.WorldRot *= Quaternion.FromToRotation(targetPose.WorldRot * Vector3.up, desiredInfo.normal);
                    return targetPose;

                }
            }
            var bestFit = GetBestFitPosition(desiredPos, maxStepLength);
            if (bestFit != null)
            {
                targetPose.WorldPos = bestFit.Item1 + footOffset;
                targetPose.WorldRot *= Quaternion.FromToRotation(targetPose.WorldRot * Vector3.up, bestFit.Item2);
                return targetPose;
            }
            bestFit = GetBestFitPosition(body.position + Vector3.down * (legLength + 0.2f), maxStepLength);
            if (bestFit != null)
            {
                targetPose.WorldPos = bestFit.Item1 + footOffset;
                targetPose.WorldRot *= Quaternion.FromToRotation(targetPose.WorldRot * Vector3.up, bestFit.Item2);
                return targetPose;
            }
            Debug.Log("Unable to find valid position for foot placement");
            return null;
        }

        [SerializeField] IKPose curPose;
        IKPose.AdjustmentType oldAdj;

        // Whether we care about the other foot finishing the step or not is decided by PlayerAnimation class
        public void Step((IKMultiPoseCycler, float, float, bool) stepParams)
        {
            curPose.Apply(this.transform);
            IKMultiPoseCycler cycler = stepParams.Item1;
            float velocity = stepParams.Item2;
            float threshold = stepParams.Item3;
            bool force = stepParams.Item4;
            oldPos = curPose.WorldPos;
            oldAdj = curPose.AdjustType;
            curPose = cycler.GetNextPoseLocalSpace(velocity - threshold, false);
            curPose.ConvertToWorldSpace();
            IKPose[] targets = cycler.GetCurrentTargetPosesWorldSpace();
            
            Vector3 relativeCenter = new Vector3(body.position.x, body.position.y - bodyH, body.position.z);
            
            if (doAdjust)
            {
                for (int i = 0; i < targets.Length; ++i)
                {
                    if (targets[i].AdjustType == IKPose.AdjustmentType.Stick || force)
                    {
                        Vector3 desiredPos = targets[i].WorldPos - relativeCenter;

                        Vector3 vel = playerAnimation.GetVelocityVector();
                        if (vel.magnitude > 0.01f)
                        {
                            // This will slightly turn the direction of our step according to our velocity
                            desiredPos = (desiredPos + (vel.normalized * velocityContribution.Evaluate(velocity))).normalized * desiredPos.magnitude + relativeCenter;
                        }
                        else
                        {
                            desiredPos = targets[i].WorldPos;
                        }
                        targets[i] = PlantTargetPosition(desiredPos, targets[i].WorldRot, targets[i].AdjustType) ?? targets[i];
                    }
                }
            }
            newPos = targets[1].WorldPos;
            cycler.AdjustCycleTargetPoses(targets[0], targets[1]);
            if (curPose.isLocalSpace)
                curForward = curPose.LocalRot * this.transform.parent.forward;
            else
                curForward = curPose.WorldRot * Vector3.forward;
            curPos = curPose.WorldPos;
            curNormal = curPose.WorldRot * Vector3.up;
            footRotation = curForward;
            curForward = AdjustFootRotation();
            curPose.WorldRot = Quaternion.FromToRotation(Vector3.forward, curForward);
            if (!grounded && doAdjust)
            {
                curPose = AdjustTargetPosition(curPose);
                if (curPose.AdjustType == IKPose.AdjustmentType.Stick && playerAnimation.GetGroundedState())
                {
                    curPose = PlantTargetPosition(curPose.WorldPos, curPose.WorldRot, curPose.AdjustType) ?? curPose;
                }
            }
            curPose.Apply(this.transform);
            Debug.Log(curPose.WorldPos);
            var groundCheckRes = GroundCheck();
            grounded = groundCheckRes.Item1;
            lerp = cycler.AnimationPosition;
            playerPos = body.position;
            playerLook = body.forward;
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
            get { return curPos; }
        }

        public Vector3 EffectorNormal
        {
            get { return curNormal; }
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

    }
}
