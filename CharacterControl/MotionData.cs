using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MLAPI.Serialization;

namespace ProceduralAnimation
{
    [Serializable]
    class PlayerMotionData : INetworkSerializable
    {
       [SerializeField] private bool initialized;

        // Rigidbody-based values

        private Vector3 velocity;
        private Vector3 angularVelocity;
        private Vector3 acceleration;
        private bool grounded; 

        [SerializeField] private Vector3 position;
        [SerializeField] private Vector3 lookDir;

        // We use momenraty values for stability reasons - its sometimes preferable to rigidbody.velocity

        private Vector3 momentaryVelocity;          // Literally the change in coordinates between FixedUpdate calls
        private float momentaryAngularVelocity;     // The change in forward vector direction between FixedUpdate calls expressed as an angle 

        public void NetworkSerialize(NetworkSerializer serializer)
        {
            serializer.Serialize(ref initialized);
            serializer.Serialize(ref velocity);
            serializer.Serialize(ref angularVelocity);
            serializer.Serialize(ref acceleration);
            serializer.Serialize(ref grounded);
            serializer.Serialize(ref position);
            serializer.Serialize(ref lookDir);
            serializer.Serialize(ref momentaryVelocity);
            serializer.Serialize(ref momentaryAngularVelocity);
        }

        public PlayerMotionData()
        {
            velocity = angularVelocity = acceleration = Vector3.zero;
            initialized = false;
        }
        public PlayerMotionData(Vector3 curVelocity, Vector3 prevVelocity, Vector3 angularVel)
        {
            velocity = curVelocity;
            acceleration = curVelocity - prevVelocity;
            angularVelocity = angularVel;
            initialized = true;
        }

        public PlayerMotionData(Rigidbody rigidBody, PlayerMotionData motionData)
        {
            velocity = rigidBody.velocity;
            angularVelocity = rigidBody.angularVelocity;
            acceleration = motionData.initialized ? velocity - motionData.velocity : Vector3.zero;
            initialized = true;
        }

        public PlayerMotionData(Rigidbody rigidBody, Transform transform, PlayerMotionData motionData)
        {
            velocity = rigidBody.velocity;
            angularVelocity = rigidBody.angularVelocity;
            if (motionData.initialized)
            {
                acceleration = velocity - motionData.velocity;
                momentaryVelocity = transform.position - motionData.position;
                momentaryAngularVelocity = Vector3.Angle(transform.forward, motionData.lookDir);
            } else
            {
                acceleration = momentaryVelocity = Vector3.zero;
                momentaryAngularVelocity = 0;
            }
            lookDir = transform.forward;
            initialized = true;
        }


        public void Update(Rigidbody rigidbody)
        {
            angularVelocity = rigidbody.angularVelocity;
            acceleration = initialized ? rigidbody.velocity - velocity : Vector3.zero;
            velocity = rigidbody.velocity;
            initialized = true;
        }

        public void Update(Rigidbody rigidbody, bool groundedState)
        {
            Update(rigidbody);
            grounded = groundedState;
        }

        public void Update(Rigidbody rigidbody, Vector3 pos)
        {
            momentaryVelocity = initialized ? pos - position : Vector3.zero;
            position = pos;
            Update(rigidbody);
        }

        public void Update(Rigidbody rigidbody, Transform transform)
        {
            momentaryAngularVelocity = initialized ? Vector3.Angle(lookDir, transform.forward) : 0;
            lookDir = transform.forward;
            Update(rigidbody, transform.position);
        }

        public void Update(Rigidbody rigidbody, Vector3 pos, bool groundedState)
        {
            Update(rigidbody, pos);
            grounded = groundedState;
        }

        public void UpdateGroundedState(bool groundedState)
        {
            grounded = groundedState;
        }

        public static float GetProjectionLength(Vector3 projection, Vector3 onDirection)
        {
            return onDirection.x != 0 ? projection.x / onDirection.x : onDirection.y != 0 ? projection.y / onDirection.y : onDirection.z != 0 ? projection.z / onDirection.z : 0;
        }

        public static float CalculateProjectionLength(Vector3 vector, Vector3 onDirection)
        {
            Vector3 projection = Vector3.Project(vector, onDirection);
            return GetProjectionLength(projection, onDirection);
        }

        public Vector3 Velocity
        {
            get { return velocity; }
        }

        public Vector3 AngularVelocity
        {
            get { return angularVelocity; }
        }

        public Vector3 Acceleration
        {
            get { return acceleration; }
        }

        public Vector3 MomentaryVelocity
        {
            get { return momentaryVelocity; }
        }

        public float MomentaryAngularVelocity
        {
            get { return momentaryAngularVelocity; }
        }

        public bool isGrounded
        {
            get { return grounded; }
        }

    }
    
    
}
