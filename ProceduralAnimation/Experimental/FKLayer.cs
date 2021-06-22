using System.Collections;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralAnimation
{
    public class FKLayer : MonoBehaviour
    {

        struct CustomJoint
        {
            public ConfigurableJoint joint { get; private set; }
            public Quaternion cachedRotation { get; private set; }
            public Transform target;

            public CustomJoint(ConfigurableJoint configurableJoint)
            {
                joint = configurableJoint;
                cachedRotation = Quaternion.identity;
                target = null;
            }

            // Use in Start method
            public void CacheRotation(Quaternion rotation)
            {
                cachedRotation = rotation;
            }


            public void Cache(Transform targetTransform)
            {
                target = targetTransform;
            }

            // Based On https://github.com/mstevenson/UnityExtensionMethods
            // Use local space since global space bounds make some movements impossible
            public void SetTargetRotation(Quaternion targetRotation)
            {

                Vector3 right = joint.axis.normalized;
                Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
                Vector3 up = Vector3.Cross(forward, right).normalized;

                Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);
                // Transform into world space
                Quaternion resultRotation = Quaternion.Inverse(worldToJointSpace);
                resultRotation *= Quaternion.Inverse(targetRotation) * cachedRotation;
                // Transform back into joint space
                resultRotation *= worldToJointSpace;
                joint.targetRotation = resultRotation;
            }

            public void UpdateRotation()
            {

                SetTargetRotation(target.localRotation);
            }

            public static explicit operator CustomJoint(ConfigurableJoint configurableJoint) => new CustomJoint(configurableJoint);
        }
        [SerializeField] PlayerAnimation playerAnimation;
        [SerializeField] Transform rootIKLayer;
        [SerializeField] Transform rootPhysicsLayer;
        List<Transform> IKLayer = new List<Transform>();
        [HideInInspector] public List<Rigidbody> physicsLayer = new List<Rigidbody>();
        List<CustomJoint> joints = new List<CustomJoint>();

        enum ForceScalingMode { Linear, Square, Cube, ClampedSquare, ClampedCubic, Huber, HuberCube, FlippedHuber };

        [SerializeField] ForceMode forceMode;
        [SerializeField] ForceScalingMode forceScaling;
        [Range(0, 1)] [SerializeField] float velocityDampening;
        
        public float followStrength;
        [SerializeField] bool debug;

       

        // Start is called before the first frame update
        void Awake()
        {

            rootIKLayer.GetComponentsInChildren<Transform>(true, IKLayer);
            if (debug)
            {
                for (int i = 0; i < IKLayer.Count; ++i)
                {
                    Debug.Log(string.Format("Added {0} to IKLayer", IKLayer[i].name));
                }
            }
            rootPhysicsLayer.GetComponentsInChildren<Rigidbody>(true, physicsLayer);
            if (debug)
            {
                float totalMass = 0;
                for (int i = 0; i < physicsLayer.Count; ++i)
                {
                    Debug.Log(string.Format("Added {0} to PhysicsLayer", physicsLayer[i].name));
                    totalMass += physicsLayer[i].mass;
                }
                Debug.Log(string.Format("Total PhysicsLayer mass {0}", totalMass));
            }

            List<Transform> tmp = new List<Transform>(IKLayer.Count);
            int idx = 0;
            for (int j = 0; j < physicsLayer.Count; ++j)
            {
                for (int i = 0; i < IKLayer.Count; ++i)
                {
                    if (IKLayer[i].gameObject.name == physicsLayer[j].gameObject.name)
                    {
                        idx = i + 1;
                        tmp.Add(IKLayer[i]);
                        if (debug)
                            Debug.Log(string.Format("Matched IKLayer {0} to {1} Physics Layer", IKLayer[i].name, physicsLayer[j].name));
                        break;
                    }
                }
            }
            IKLayer = tmp;

            joints = new List<CustomJoint>(physicsLayer.Count);
            for (int i = 0; i < physicsLayer.Count; ++i)
            {
                var configurableJoint = physicsLayer[i].GetComponent<ConfigurableJoint>();
                if (configurableJoint != null)
                {
                    //configurableJoint.configuredInWorldSpace = true;
                    CustomJoint tmpJoint = new CustomJoint(configurableJoint);
                    tmpJoint.Cache(IKLayer[i]);
                    joints.Add(tmpJoint);
                    if (debug)
                    {
                        Debug.Log(string.Format("Added joint {0} to PhysicsLayer", configurableJoint.name));
                    }
                }
            }
        }
        private void Start()
        {
            // Cache joint rotations for further use internally in SetTargetRotation method
            for (int i = 0; i < joints.Count; ++i)
            {
                joints[i].CacheRotation(joints[i].joint.transform.localRotation);
            }
            playerAnimation.InitializeRagdoll(physicsLayer.ToArray());
        }



        Vector3 CalcForce(Vector3 distance)
        {
            switch (forceScaling)
            {
                case ForceScalingMode.Square:
                    return distance.normalized * distance.sqrMagnitude;
                case ForceScalingMode.Cube:
                    return distance.normalized * distance.sqrMagnitude * distance.magnitude;
                case ForceScalingMode.ClampedSquare:
                    return distance.normalized * Mathf.Min(distance.sqrMagnitude, 1);
                case ForceScalingMode.ClampedCubic:
                    return distance.normalized * Mathf.Min(distance.sqrMagnitude * distance.magnitude, 1);
                case ForceScalingMode.Huber:
                    return distance.normalized * (distance.magnitude <= 1 ? 0.5f * distance.sqrMagnitude : distance.magnitude - 0.5f);
                case ForceScalingMode.HuberCube:
                    return distance.normalized * (distance.sqrMagnitude <= 1 ? 2 / 3f * distance.sqrMagnitude * distance.magnitude : distance.sqrMagnitude - 1 / 3f);
                case ForceScalingMode.FlippedHuber:
                    return distance.normalized * (distance.sqrMagnitude <= 1 ? 2.0f * distance.magnitude : distance.sqrMagnitude + 1.0f);
                default:
                    return distance;
            }
        }

        void DampenLimb(ref Rigidbody rb)
        {
            Vector3 velocity = rb.velocity;
            rb.velocity.Set(0, 0, 0);
            rb.Sleep();
            rb.AddForce(Vector3.ProjectOnPlane(velocity, Vector3.up) * velocityDampening + Vector3.Project(velocity, Vector3.up), ForceMode.VelocityChange);
        }

        void ApplyForce(ref Rigidbody rb, Transform target)
        {
            rb.AddForce(followStrength * CalcForce(target.position - rb.gameObject.transform.position), forceMode);
        }

        // Update is called once per frame
        void FixedUpdate()
        {
            Rigidbody rigidbody;
            CustomJoint joint;
            for (int i = 0; i < physicsLayer.Count; ++i)
            {
                rigidbody = physicsLayer[i];
                DampenLimb(ref rigidbody);
                ApplyForce(ref rigidbody, IKLayer[i]);
                physicsLayer[i] = rigidbody;
            }
            for (int i = 0; i < joints.Count; ++i)
            {
                joint = joints[i];
                joint.UpdateRotation();
                joints[i] = joint;
            }
        }
    }
}
