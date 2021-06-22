using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralAnimation
{
    [Serializable]
    struct StepParams
    {
        [SerializeField] float stepLength, stepHeight, stepDistance, speed;
        float newStepLength, newStepHeight, newStepDistance, newSpeed;
        bool mutatedParams;

        StepParams(float length, float height, float distance, float s)
        {
            stepLength = newStepLength = length;
            stepHeight = newStepHeight = height;
            stepDistance = newStepDistance = distance;
            speed = newSpeed = s;
            mutatedParams = false;
        }

        private static void Update(ref float curVal, float newVal, ref float tempStorage, ref bool mutatedState)
        {
            mutatedState |= (Mathf.Abs(curVal - newVal) > Mathf.Epsilon);
            if (mutatedState)
            {
                tempStorage = newVal;
            }
        }

        public float StepLength
        {
            get { return stepLength; }
            set { Update(ref stepLength, value, ref newStepLength, ref mutatedParams); }
        }

        public float StepHeight
        {
            get { return stepHeight; }
            set { Update(ref stepHeight, value, ref newStepHeight, ref mutatedParams); }
        }

        public float StepDistance
        {
            get { return stepDistance; }
            set { Update(ref stepDistance, value, ref newStepDistance, ref mutatedParams); }
        }

        public float StepSpeed
        {
            get { return speed; }
            set { Update(ref speed, value, ref newSpeed, ref mutatedParams); }
        }

        public bool isMutated
        {
            get { return mutatedParams; }
        }

        public void TryApplyMutation()
        {
            if (mutatedParams)
            {
                stepLength = newStepLength;
                stepHeight = newStepHeight;
                stepDistance = newStepDistance;
                speed = newSpeed;
                mutatedParams = false;
            }
        }

    }
}