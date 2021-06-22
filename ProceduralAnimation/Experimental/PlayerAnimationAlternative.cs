using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAPI;
using MLAPI.NetworkVariable;

namespace ProceduralAnimation
{
    public class PlayerAnimationAlternative : NetworkBehaviour
    {
        [SerializeField] PlayerManager playerManager;
        [SerializeField] NetworkVariable<PlayerMotionData> motionData = new NetworkVariable<PlayerMotionData>(new NetworkVariableSettings { WritePermission = NetworkVariablePermission.OwnerOnly, ReadPermission = NetworkVariablePermission.Everyone });
        [SerializeField] Transform playerModel;
        [SerializeField] Transform modelPelvis;
        [SerializeField] [Range(0, 1)] float ragdollStrength;
        [SerializeField] bool dampen;
        [SerializeField] IKFootSolverAlternative leftFootSolver;
        [SerializeField] IKFootSolverAlternative rightFootSolver;
        [SerializeField] FKLayer FKLayer;
        [SerializeField] float runThreshold;
        [SerializeField] IKCyclerMeld walkCycler;
        [SerializeField] IKCyclerMeld otherFootWalkCycler;
        [SerializeField] bool setupInWorldSpace = false;
        [SerializeField] AnimationCurve velocityToStepBlend;
        [SerializeField] AnimationCurve velocityToStepCycleFreq;

        [SerializeField] AnimationCurve angularVelocityToPoseBlend;
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

            walkCycler.SetActive(0, 1);
            otherFootWalkCycler.SetActive(0, 1);

            walkCycler.SetLocalSpaceTransformer(leftFootSolver.transform);
            otherFootWalkCycler.SetLocalSpaceTransformer(rightFootSolver.transform);
            // Setup for synchronous out of phase updates
            otherFootWalkCycler.SyncWith(walkCycler, 0.5f);
            otherFootWalkCycler.ResetCyclePoses();

            if (setupInWorldSpace)
            {
                walkCycler.ConvertToLocalSpace(leftFootSolver.transform);
                otherFootWalkCycler.ConvertToLocalSpace(rightFootSolver.transform);
            }
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
            Quaternion newRot = (Quaternion.AngleAxis(-amount, playerModel.worldToLocalMatrix * Vector3.Cross(planarAcceration, Vector3.up).normalized));

            playerModel.localRotation = ClampRotation(newRot, new Vector3(maxAmount, 0.01f, maxAmount));
            if (Vector3.ProjectOnPlane(motionData.Value.Velocity, Vector3.up).magnitude < 0.01f)
            {
                playerModel.rotation = Quaternion.Slerp(playerModel.rotation, gameObject.transform.rotation, 3.0f * Time.fixedDeltaTime);
            }
        }

        bool requiresSync = false;

        void FixedUpdate()
        {
            if (playerManager.useNetworkMLAPI && IsLocalPlayer)
            {
                Debug.Log("Stuff");
            }

            // Anticipate velocity for the next fixedupdate to create more believable motion
            // anticipation is vital for plausable results but has edge cases
            float anticipatedSpeed = PredictVelocityVector(Time.fixedDeltaTime).magnitude;

           
            if (dampen)
                AdjustFroces();
            float tilt = accelerationTilt.Evaluate(motionData.Value.Acceleration.magnitude);
            ApplyTilt(tilt);
            requiresSync = false;
            if (anticipatedSpeed < runThreshold && (walkCycler.GetActiveCyclers() == null || walkCycler.GetActiveCyclers()[1] == 2))
            {
                walkCycler.SetActive(0, 1);
                otherFootWalkCycler.SetActive(0, 1);
                walkCycler.Synchronise(0);
                otherFootWalkCycler.Synchronise(0);
                requiresSync = true;
            }
            else if (anticipatedSpeed >= runThreshold && walkCycler.GetActiveCyclers()[1] != 2)
            {
                walkCycler.SetActive(1, 2);
                otherFootWalkCycler.SetActive(1, 2);
                walkCycler.Synchronise(2);
                otherFootWalkCycler.Synchronise(2);
                requiresSync = true;
            }
            Debug.Log(walkCycler.GetActiveCyclers());
            float cycleFreq = velocityToStepCycleFreq.Evaluate(anticipatedSpeed);
            walkCycler.AdjustCycleSpeed(cycleFreq * Time.fixedDeltaTime);
            otherFootWalkCycler.AdjustCycleSpeed(cycleFreq * Time.fixedDeltaTime);

            // Use Momentary values for stability reasons
            if (motionData.Value.MomentaryVelocity.magnitude > 0.01f || motionData.Value.MomentaryAngularVelocity > 0.01f ||
                Vector3.Angle(leftFootSolver.LastUpdatedLook, playerModel.transform.forward) > 5.0f ||
                Vector3.Angle(rightFootSolver.LastUpdatedLook, playerModel.transform.forward) > 5.0f ||
                Vector3.Distance(leftFootSolver.LastUpdatedPosition, playerModel.transform.position) > 0.1f ||
                Vector3.Distance(rightFootSolver.LastUpdatedPosition, playerModel.transform.position) > 0.1f)
            {
                leftFootSolver.Step((walkCycler, anticipatedSpeed, anticipatedSpeed >= runThreshold ? runThreshold : 0.0f, false));
                rightFootSolver.Step((otherFootWalkCycler, anticipatedSpeed, anticipatedSpeed >= runThreshold ? runThreshold : 0.0f, false));
            }
            else
            {
                // Reset when needed
                walkCycler.AnimationSkipTo(0);
                otherFootWalkCycler.AnimationSkipTo(0.5f);
            }
        }
    }
}
