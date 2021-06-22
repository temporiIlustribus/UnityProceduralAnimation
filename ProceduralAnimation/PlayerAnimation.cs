using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAPI;
using MLAPI.NetworkVariable;

namespace ProceduralAnimation
{
    
    public class PlayerAnimation : NetworkBehaviour
    {
        [SerializeField] PlayerManager playerManager;
        [SerializeField] NetworkVariable<PlayerMotionData> motionData = new NetworkVariable<PlayerMotionData>(new NetworkVariableSettings { WritePermission = NetworkVariablePermission.OwnerOnly, ReadPermission = NetworkVariablePermission.Everyone });
        [SerializeField] Transform playerModel;
        [SerializeField] Transform modelPelvis;
        [SerializeField] [Range(0, 1)] float ragdollStrength;
        [SerializeField] bool dampen;
        [SerializeField] IKFootSolver leftFootSolver;
        [SerializeField] IKFootSolver rightFootSolver;
        [SerializeField] FKLayer FKLayer;
        [SerializeField] float runThreshold;
        [SerializeField] AnimationCurve velocityToStepLength;
        [SerializeField] AnimationCurve velocityToStepHeight;
        [SerializeField] AnimationCurve velocityToStepDistance;
        [SerializeField] AnimationCurve velocityToStepPeriod;

        [SerializeField] AnimationCurve velocityToHipPos;
        [SerializeField] AnimationCurve velocityToAngle;
        [SerializeField] AnimationCurve accelerationTilt;
        [SerializeField] [Range(0, 90)] float maxTiltAngle;
        [SerializeField] bool disableRagdoll = false;

        private Rigidbody[] ragdollRigidbodies;
        float stepL, stepH, stepD, stepT;
        uint counter = 0;

        public void UpdateMotionData(Rigidbody rigidbody, Transform transform, bool grounded)
        {
            motionData.Value.Update(rigidbody, transform);
            motionData.Value.UpdateGroundedState(grounded);
        }

        public Vector3 GetVelocityVector()
        {
            return motionData.Value.Velocity;
        }

        public Vector3 PredictVelocityVector(float t)
        {
            return motionData.Value.Velocity + t * motionData.Value.Acceleration;
        }

        // Predict vector direction according to current motion data
        public Vector3 PredictVectorDir(Vector3 vec, float t, float drag = 0.05f)
        {
            return vec + motionData.Value.AngularVelocity * t * (1 - drag * t); // Adjusted for angular drag
        }

        public Vector3 GetAngularVelocity()
        {
            return motionData.Value.AngularVelocity;
        }

        public float GetMomentaryAngularVelocity()
        {
            return motionData.Value.MomentaryAngularVelocity;
        }

        public bool GetGroundedState()
        {
            return motionData.Value.isGrounded;
        }
        
        void Awake()
        {

            leftFootSolver.Setup();
            rightFootSolver.Setup();

            leftFootSolver.PlantFoot();
            leftFootSolver.PlantFoot();
        }

        public void InitializeRagdoll(Rigidbody[] rigidbodies)
        {
            ragdollRigidbodies = rigidbodies;
        }


        Vector3 rbVelocity;

        void AdjustFroces()
        {

            for (int i = 0; i < ragdollRigidbodies.Length; ++i)
            {
                rbVelocity = ragdollRigidbodies[i].velocity * ragdollStrength;
                ragdollRigidbodies[i].angularVelocity *= ragdollStrength;
                ragdollRigidbodies[i].Sleep();
                ragdollRigidbodies[i].velocity = rbVelocity;
            }
        }

        void ApplyTilt(float amount)
        {
            Quaternion ClampRotation(Quaternion q, Vector3 bounds)
            {
                q.x /= q.w;
                q.y /= q.w;
                q.z /= q.w;
                q.w = 1.0f;

                float angleX = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.x);
                angleX = Mathf.Clamp(angleX, -bounds.x, bounds.x);
                q.x = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleX);

                float angleY = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.y);
                angleY = Mathf.Clamp(angleY, -bounds.y, bounds.y);
                q.y = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleY);

                float angleZ = 2.0f * Mathf.Rad2Deg * Mathf.Atan(q.z);
                angleZ = Mathf.Clamp(angleZ, -bounds.z, bounds.z);
                q.z = Mathf.Tan(0.5f * Mathf.Deg2Rad * angleZ);

                return q.normalized;
            }
            Vector3 planarAcceration = Vector3.ProjectOnPlane(motionData.Value.Acceleration, Vector3.up);
            float maxAmount = maxTiltAngle - Vector3.Angle(playerModel.up, Vector3.up);
            Quaternion newRot = (playerModel.localRotation * Quaternion.AngleAxis(-amount, playerModel.worldToLocalMatrix * Vector3.Cross(planarAcceration, Vector3.up).normalized));
             
            playerModel.localRotation = ClampRotation(newRot, new Vector3(maxAmount, 0.01f, maxAmount));
            if (Vector3.ProjectOnPlane(motionData.Value.Velocity, Vector3.up).magnitude  < 0.01f)
            {
                playerModel.rotation = Quaternion.Slerp(playerModel.rotation, gameObject.transform.rotation, 3.0f * Time.fixedDeltaTime);
            }
        }

        
        void FixedUpdate()
        {
            Debug.Log(motionData.Value);
            // Anticipate velocity for the next fixedupdate to create more believable motion 
            float anticipatedSpeed = PredictVelocityVector(Time.fixedDeltaTime).magnitude;
            // Fundamental difference between running and walking is that when running 
            // both feet can be off the ground at the same time
            bool runState = anticipatedSpeed >= runThreshold;
            stepL = velocityToStepLength.Evaluate(anticipatedSpeed);
            stepH = velocityToStepHeight.Evaluate(anticipatedSpeed);
            stepD = velocityToStepDistance.Evaluate(anticipatedSpeed);
            stepT = velocityToStepPeriod.Evaluate(anticipatedSpeed);
            leftFootSolver.UpdateStepParameters(stepL, stepH, stepD, stepT);
            rightFootSolver.UpdateStepParameters(stepL, stepH, stepD, stepT);
            if (dampen)
                AdjustFroces();
            float tilt = accelerationTilt.Evaluate(motionData.Value.Acceleration.magnitude);
            ApplyTilt(tilt);
            // Use Momentary values for stability reasons
            if (motionData.Value.MomentaryVelocity.magnitude > 0.01f || motionData.Value.MomentaryAngularVelocity > 0.01f ||
                Vector3.Angle(leftFootSolver.LastUpdatedLook, playerModel.transform.forward) > 10.0f ||
                Vector3.Angle(rightFootSolver.LastUpdatedLook, playerModel.transform.forward) > 10.0f ||
                Vector3.Distance(leftFootSolver.LastUpdatedPosition, playerModel.transform.position) > 0.1f ||
                Vector3.Distance(rightFootSolver.LastUpdatedPosition, playerModel.transform.position) > 0.1f)
            {
                bool flag = false;
                if (counter == 0U)
                {
                    flag = leftFootSolver.isAnimating();
                    leftFootSolver.Step(runState);
                    rightFootSolver.Step(runState);
                    // Flip between which leg gets updated first based on when each finishes a step
                    counter = flag && !leftFootSolver.isAnimating() ? 1U : 0U;
                }
                else
                {
                    flag = rightFootSolver.isAnimating();
                    rightFootSolver.Step(runState);
                    leftFootSolver.Step(runState);
                    counter = flag && !rightFootSolver.isAnimating() ? 0U : 1U;
                }
            }
            else
            {
                if (leftFootSolver.isAnimating())
                {
                    leftFootSolver.Step();
                    rightFootSolver.Step();
                    counter = leftFootSolver.isAnimating() ? 1U : 0U;
                }
                if (rightFootSolver.isAnimating())
                {
                    rightFootSolver.Step();
                    leftFootSolver.Step();
                    counter = !rightFootSolver.isAnimating() ? 0U : 1U;
                }
            }
        }
    }
}
