using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;

namespace ProceduralAnimation
{
    interface IPoseBlender<Pose> where Pose : IPose<Pose>, new()
    {
        Pose Blend(Pose pose1, Pose pose2, float t);
    }

    interface IPoseCycler<Pose, PoseBlender, Container>
        where PoseBlender : IPoseBlender<Pose>
        where Container : IEnumerable<Pose>
        where Pose : IPose<Pose>, new()
    {
        Pose GetNextPose();
        Pose GetCurrentTargetPose();
        Pose GetCurrentTargetPoseLocalSpace(bool set);
        Pose GetCurrentTargetPoseWorldSpace(bool set);
        void AdjustCycleTargetPose(Pose newPose);
        void ResetCyclePoses();
        Container Poses
        {
            get;
            set;
        }
        Container CyclePoses
        {
            get;
            set;
        }
        float AnimationPosition
        {
            get;
        }
        float AnimationSkip();
        void AnimationSkipTo(float position);
    }

    interface IMultiPoseCycler<PoseCycler, Pose, PoseBlender, Container>
        where PoseCycler : IPoseCycler<Pose, PoseBlender, Container>
        where PoseBlender : IPoseBlender<Pose>
        where Container : IEnumerable<Pose>
        where Pose : IPose<Pose>, new()
    {
        Pose GetNextPose(int first, int second, float blendPos);
        Pose GetNextPose(float blendPos);
        Pose GetNextPoseWorldSpace(float blendPos, bool set);
        Pose GetNextPoseLocalSpace(float blendPos, bool set);

        void ConvertToLocalSpace(Transform transform);
        void ConvertToWorldSpace(Transform transform);
        void ResetCyclePoses(int index);

        PoseCycler this[int i] { get; set; }

        Pose[] GetCurrentTargetPoses();
        Pose[] GetCurrentTargetPosesWorldSpace(bool set);
        Pose[] GetCurrentTargetPosesLocalSpace(bool set);

        void AdjustCycleTargetPose(Pose newPose);
        void SetLocalSpaceTransformer(Transform transform);
        void AdjustCycleSpeed(float newSpeed);
        float AnimationSkip();
        void AnimationSkipTo(float position);
    }

    /* Some notes on Pose Blenders and pose cycle blenders:
     * 
     * While these classes are not intended to be used with animations that use a large number of poses 
     * they do use parallelism where possible (and/or where it is sensible to do so). 
     * 
     * Wherever parallelism is used, we just allow C# Task scheduller  to take over 
     * meaning we don't explicitly partition tasks or use any estimates - the scheduller
     * knows best how to run all operations most efficiently.
     * The Thresholds for executing everything in Parallel were chosen based on times measured 
     * on a 2-core CPU.
     */


    [Serializable]
    public class IKPoseBlender : IPoseBlender<IKPose>
    {
        [SerializeField] AnimationCurve positionBlend;
        [SerializeField] AnimationCurve rotationBlend;

        public IKPoseBlender()
        {
            positionBlend = new AnimationCurve(new Keyframe(0.0f, 0.0f), new Keyframe(1.0f, 1.0f));
            rotationBlend = new AnimationCurve(new Keyframe(0.0f, 0.0f), new Keyframe(1.0f, 1.0f));
        }

        public IKPoseBlender(IKPoseBlender other)
        {
            positionBlend = other.positionBlend;
            rotationBlend = other.rotationBlend;
        }

        public IKPoseBlender(AnimationCurve posCurve, AnimationCurve rotCurve)
        {
            positionBlend = posCurve;
            rotationBlend = rotCurve;
        }

        /* Get a blend of the two poses using rotation and position curves 
         * - t is in [0, 1] range and controls position on the curves
         */

        public IKPose Blend(IKPose pose1, IKPose pose2, float t)
        {
            return pose1.Blend(pose2, positionBlend.Evaluate(t), rotationBlend.Evaluate(t));
        }

        public IKPose BlendWorldSpace(IKPose pose1, IKPose pose2, float t)
        {
            return pose1.BlendWorld(pose2, positionBlend.Evaluate(t), rotationBlend.Evaluate(t));
        }

        public IKPose BlendLocalSpace(IKPose pose1, IKPose pose2, float t)
        {
            return pose1.BlendLocal(pose2, positionBlend.Evaluate(t), rotationBlend.Evaluate(t));
        }
    }

    [Serializable]
    public class IKPoseCycler : IPoseCycler<IKPose, IKPoseBlender, List<IKPose>>
    {
        protected int parallelThreshold = 8;

        // Typically we use few animation poses fo our motion and mainly use curves to control the flow of an animation
        // This makes the approach of keeping a separate list of poses for current cycle reasonable
        [SerializeField] List<IKPose> poses;
        [SerializeField] List<IKPoseBlender> multiBlender;
        [SerializeField] float t;
        [SerializeField] float speed;

        List<IKPose> cyclePoses;

        private List<IKPose> DeepCopy(IEnumerable<IKPose> poses)
        {
            // Fun Linq trick
            List<IKPose> posesCopy = poses.Select(x => new IKPose(x)).ToList();
            return posesCopy;
        }

        public IKPoseCycler()
        {
            poses = new List<IKPose>();
            cyclePoses = new List<IKPose>();
            multiBlender = new List<IKPoseBlender>();
            t = 0;
            speed = 0.001f;
        }

        public IKPoseCycler(IKPoseCycler other)
        {
            poses = new List<IKPose>(other.poses.Count);
            cyclePoses = new List<IKPose>(other.poses.Count);
            multiBlender = new List<IKPoseBlender>(other.multiBlender.Count);
            for (int i = 0; i < other.poses.Count; ++i)
            {
                poses.Add(new IKPose(other.poses[i]));
                cyclePoses.Add(new IKPose(other.poses[i]));
                multiBlender.Add(new IKPoseBlender(other.multiBlender[i]));
            }

            t = other.t;
            speed = other.speed;
        }

        public IKPoseCycler(IEnumerable<IKPose> iKPoses, IEnumerable<IKPoseBlender> blender, float cycleSpeed = 0.001f)
        {
            poses = DeepCopy(iKPoses);
            cyclePoses = DeepCopy(iKPoses);
            multiBlender = new List<IKPoseBlender>(blender.Count());
            if (multiBlender.Count != poses.Count)
            {
                throw new ArgumentException(string.Format("The number of animation curves and IK poses must be equal for IKPoseCycler to work.\nProvided arrays had lengths {0} (IK Pose array), {1} (AnimationCurve array).",
                    poses.Count, multiBlender.Count));
            }
            for (int i = 0; i < blender.Count(); ++i)
            {
                multiBlender.Add(new IKPoseBlender(multiBlender.ElementAt(i)));
            }
            t = 0;
            speed = cycleSpeed;
        }

        public IKPoseCycler(IEnumerable<IKPose> iKPoses, IKPoseBlender blender, float cycleSpeed = 0.001f)
        {
            poses = new List<IKPose>(iKPoses);
            cyclePoses = DeepCopy(iKPoses);
            multiBlender = new List<IKPoseBlender>(poses.Count);
            for (int i = 0; i < poses.Count; ++i)
            {
                multiBlender.Add(new IKPoseBlender(blender));
            }
            t = 0;
            speed = cycleSpeed;
        }

        public void ConvertToLocalSpace(Transform transform)
        {
            if (poses.Count() > parallelThreshold)
            {
                Parallel.For(0, poses.Count(), (i, state) =>
                {
                    poses[i].ConvertToLocalSpace(transform);
                });
            }
            else
            {
                for (int j = 0; j < poses.Count(); ++j)
                {
                    poses[j].ConvertToLocalSpace(transform);
                }
            }
        }

        public void ConvertToWorldSpace(Transform transform)
        {
            if (poses.Count() > parallelThreshold)
            {
                Parallel.For(0, poses.Count(), (i, state) =>
                {
                    poses[i].ConvertToWorldSpace(transform);
                });
            } 
            else
            {
                for (int j = 0; j < poses.Count(); ++j)
                {
                    poses[j].ConvertToWorldSpace(transform);
                }
            }
        }

        public void ResetCyclePoses()
        {
            // Save on GC time
            if (cyclePoses is null || cyclePoses.Count != poses.Count)
            {
                cyclePoses = DeepCopy(poses);
            }
            else
            {
                if (poses.Count() > parallelThreshold)
                {
                    Parallel.For(0, poses.Count(), (j, state) =>
                    {
                        cyclePoses[j] = poses[j];
                    });
                }
                else
                {
                    for (int j = 0; j < poses.Count(); ++j)
                    {
                        cyclePoses[j] = poses[j];
                    }
                }
            }
        }

        // Auto cycles through all poses using blend curve
        public IKPose GetNextPose()
        {
            // Reset after a cycle completes
            if (t >= 1.0f || t <= 0.0f)
            {
                t = 0.0f;
                ResetCyclePoses();
            }
            if (poses.Count == 1)
            {
                t += speed;
                return cyclePoses[0];
            }
            int index = Mathf.FloorToInt(poses.Count * t);
            float curVal = (t - (index * 1.0f / poses.Count)) * poses.Count;
            t += speed;
            if (index < poses.Count - 1)
            {
                return multiBlender[index].Blend(cyclePoses[index], cyclePoses[index + 1], curVal);
            }
            return multiBlender[cyclePoses.Count - 1].Blend(cyclePoses[cyclePoses.Count - 1], cyclePoses[0], curVal);
        }

        public IKPose GetNextPoseWorldSpace(bool set = true)
        {
            if (!set)
                return GetNextPose().GetWorldSpace();
            if (t >= 1.0f || t <= 0.0f)
            {
                t = 0.0f;
                ResetCyclePoses();
            }
            if (poses.Count == 1)
            {
                t += speed;
                cyclePoses[0].ConvertToWorldSpace();
                return cyclePoses[0];
            }
            int index = Mathf.FloorToInt(poses.Count * t);
            float curVal = (t - (index * 1.0f / poses.Count)) * poses.Count;
            t += speed;
            if (index < poses.Count - 1)
            {
                cyclePoses[index].ConvertToWorldSpace();
                cyclePoses[index + 1].ConvertToWorldSpace();
                return multiBlender[index].Blend(cyclePoses[index], cyclePoses[index + 1], curVal);
            }
            cyclePoses[cyclePoses.Count - 1].ConvertToWorldSpace();
            cyclePoses[0].ConvertToWorldSpace();
            return multiBlender[cyclePoses.Count - 1].Blend(cyclePoses[cyclePoses.Count - 1], cyclePoses[0], curVal);
        }

        public IKPose GetNextPoseLocalSpace(bool set = true)
        {
            if (!set)
                return GetNextPose().GetLocalSpace();
            if (t >= 1.0f || t <= 0.0f)
            {
                t = 0.0f;
                ResetCyclePoses();
            }
            if (poses.Count == 1)
            {
                t += speed;
                cyclePoses[0].ConvertToLocalSpace();
                return cyclePoses[0];
            }
            int index = Mathf.FloorToInt(poses.Count * t);
            float curVal = (t - (index * 1.0f / poses.Count)) * poses.Count;
            t += speed;
            if (index < poses.Count - 1)
            {
                cyclePoses[index].ConvertToLocalSpace();
                cyclePoses[index + 1].ConvertToLocalSpace();
                return multiBlender[index].Blend(cyclePoses[index], cyclePoses[index + 1], curVal);
            }
            cyclePoses[cyclePoses.Count - 1].ConvertToLocalSpace();
            cyclePoses[0].ConvertToLocalSpace();
            return multiBlender[cyclePoses.Count - 1].Blend(cyclePoses[cyclePoses.Count - 1], cyclePoses[0], curVal);
        }

        public IKPose GetCurrentTargetPose()
        {
            if (t >= 1.0f || t <= 0.0f)
            {
                t = 0.0f;
                ResetCyclePoses();
            }
            int index = Mathf.FloorToInt(cyclePoses.Count * t);
            if (index < cyclePoses.Count - 1)
                return cyclePoses[index + 1];
            return cyclePoses[0];
        }

        public IKPose GetCurrentTargetPoseWorldSpace(bool set = true)
        {
            if (t >= 1.0f || t <= 0.0f)
            {
                t = 0.0f;
                ResetCyclePoses();
            }
            int index = Mathf.FloorToInt(cyclePoses.Count * t);
            if (index < cyclePoses.Count - 1)
            {
                if (!set)
                    return cyclePoses[index + 1].GetWorldSpace();
                cyclePoses[index + 1].ConvertToWorldSpace();
                return cyclePoses[index + 1];
            }
            if (!set)
                return cyclePoses[0].GetWorldSpace();
            cyclePoses[0].ConvertToWorldSpace();
            return cyclePoses[0];
        }

        public IKPose GetCurrentTargetPoseLocalSpace(bool set = true)
        {
            if (t >= 1.0f || t <= 0.0f)
            {
                t = 0.0f;
                ResetCyclePoses();
            }
            int index = Mathf.FloorToInt(cyclePoses.Count * t);
            if (index < cyclePoses.Count - 1)
            {
                if (!set)
                    return cyclePoses[index + 1].GetLocalSpace();
                cyclePoses[index + 1].ConvertToLocalSpace();
                return cyclePoses[index + 1];
            }
            if (!set)
                return cyclePoses[0].GetLocalSpace();
            cyclePoses[0].ConvertToLocalSpace();
            return cyclePoses[0];
        }

        // Adjust current target pose just for this cycle only
        public void AdjustCycleTargetPose(IKPose newPose)
        {
            if (t >= 1.0f || t <= 0.0f)
            {
                t = 0.0f;
                ResetCyclePoses();
            }
            int index = Mathf.FloorToInt(cyclePoses.Count * t);
            if (index < cyclePoses.Count - 1)
            {
                cyclePoses[index + 1] = newPose;
            }
            else
            {
                cyclePoses[0] = newPose;
            }
        }

        public List<IKPose> Poses
        {
            get { return poses; }
            set
            {
                if (multiBlender.Count != value.Count)
                {
                    throw new ArgumentException(string.Format("The number of animation curves and IK poses must be equal for IKPoseCycler to work.\n Provided arrays had lengths {0} (IK Pose array), {1} (AnimationCurve array).",
                        value.Count, multiBlender.Count));
                }
                poses = value;

            }
        }

        public List<IKPose> CyclePoses
        {
            get { return cyclePoses; }
            set
            {
                if (multiBlender.Count != value.Count)
                {
                    throw new ArgumentException(string.Format("The number of animation curves and IK poses must be equal for IKPoseCycler to work.\n Provided arrays had lengths {0} (IK Pose array), {1} (AnimationCurve array).",
                        value.Count, multiBlender.Count));
                }
                cyclePoses = value;
            }
        }

        public void SetLocalSpaceTransformer(Transform transform)
        {
            if (poses.Count > parallelThreshold)
            {
                Parallel.For(0, poses.Count, (k, state) =>
                {
                    IKPose pose = poses[k];
                    pose.LocalSpaceTransformer = transform;
                    poses[k] = pose;
                });
            }
            else
            {
                for (int k = 0; k < poses.Count; ++k)
                {
                    IKPose pose = poses[k];
                    pose.LocalSpaceTransformer = transform;
                    poses[k] = pose;
                }
            }
        }

        // Speed measured as step per AnimationSample call
        public float CycleSpeed
        {
            get { return speed; }
            set { speed = value; }
        }

        // Instead of setting cycle speed directly adjusts the speed such that
        // Between each of the poses there is atleast [subdivisions] frames
        public void AdjustRelativeCycleSpeed(uint subdivisions)
        {
            speed = 1 / (poses.Count * subdivisions);
        }

        public float AnimationPosition
        {
            get { return t; }
        }

        public float AnimationSkip()
        {
            t += speed;
            return t;
        }

        public void AnimationSkipTo(float position)
        {
            float delta = t - position;
            if (delta > Mathf.Epsilon || delta <= -1.0f)
            {
                ResetCyclePoses();
            }
            t = position;
        }
    }

    /* Pose blend over pose cyclers 
     * Example - blend between run and walk cycles
     * Can keep track of currently active cyclers for blending between 2 pose cycles
     * Supports Blending between poses of multiple walk cyclers and propagating an adjustment over current cycles
     */
    [Serializable]
    public class IKMultiPoseCycler : IMultiPoseCycler<IKPoseCycler, IKPose, IKPoseBlender, List<IKPose>>
    {
        protected int parallelThreshold = 8;

        [SerializeField] List<IKPoseCycler> cyclers;
        [SerializeField] IKPoseBlender blender;
        [SerializeField] float t;
        float speed;
        [SerializeField] int firstActive, secondActive; // Keep track of cycles we want to blend betweeen


        public IKMultiPoseCycler()
        {
            t = 0.0f;
            firstActive = -1;
            secondActive = -1;
            speed = 0.001f;
            cyclers = new List<IKPoseCycler>();
            blender = new IKPoseBlender();
        }

        public IKMultiPoseCycler(IKMultiPoseCycler cycler)
        {
            t = cycler.t;
            firstActive = cycler.firstActive;
            secondActive = cycler.secondActive;
            speed = cycler.speed;
            cyclers = new List<IKPoseCycler>(cycler.cyclers.Count);
            for (int i = 0; i < cycler.cyclers.Count; ++i)
            {
                cyclers.Add(new IKPoseCycler(cycler.cyclers[i]));
            }
            blender = new IKPoseBlender(cycler.blender);
        }

        public IKMultiPoseCycler(IKPoseCycler[] poseCyclers, float cycleSpeed = 0.001f)
        {
            t = 0;
            speed = cycleSpeed;
            firstActive = secondActive = -1;
            cyclers = new List<IKPoseCycler>(poseCyclers);
            blender = new IKPoseBlender();
        }

        public IKMultiPoseCycler(IKPoseCycler[] poseCyclers, IKPoseBlender poseBlender, float cycleSpeed = 0.001f)
        {
            t = 0;
            speed = cycleSpeed;
            cyclers = new List<IKPoseCycler>(poseCyclers);
            blender = poseBlender;
        }

        public void ConvertToLocalSpace(Transform transform)
        {
            if (cyclers.Count > parallelThreshold)
            {
                Parallel.For(0, cyclers.Count(), (i, state) =>
                {
                    cyclers[i].ConvertToLocalSpace(transform);
                });
            }
            else
            {
                for (int i = 0; i < cyclers.Count; ++i)
                {
                    cyclers[i].ConvertToLocalSpace(transform);
                }
            }
        }

        public void ConvertToWorldSpace(Transform transform)
        {
            if (cyclers.Count > parallelThreshold)
            {
                Parallel.For(0, cyclers.Count(), (i, state) =>
                {
                    cyclers[i].ConvertToWorldSpace(transform);
                });
            }
            else
            {
                for (int i = 0; i < cyclers.Count; ++i)
                {
                    cyclers[i].ConvertToWorldSpace(transform);
                }
            }
        }

        public void ResetCyclePoses(int index)
        {
            cyclers[index].ResetCyclePoses();
        }

        public void ResetCyclePoses()
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler.ResetCyclePoses() can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            cyclers[firstActive].ResetCyclePoses();
            cyclers[secondActive].ResetCyclePoses();
        }

        public IKPoseCycler this[int i]
        {
            get { return cyclers[i]; }
            set { cyclers[i] = value; }
        }

        public List<IKPoseCycler> Cyclers
        {
            get { return cyclers; }
            set { cyclers = value; }
        }

        public IKPoseBlender Blender
        {
            get { return blender; }
            set { blender = value; }
        }

        public int CyclerCount
        {
            get { return cyclers.Count; }
        }

        public float Speed
        {
            get { return speed; }
            protected set { speed = value; }
        }

        public void Synchronise(int index)
        {
            if (Mathf.Abs(cyclers[index].AnimationPosition - t) > Mathf.Epsilon)
                cyclers[index].AnimationSkipTo(t);

        }

        // Set active IKCyclers
        public void SetActive(int first, int second)
        {
            AdjustCycleSpeed(first, second, speed);
            firstActive = first;
            secondActive = second;
            Synchronise(firstActive);
            Synchronise(secondActive);
            ResetCyclePoses();
        }

        // Deactivate IKCyclers
        public void SetInactive()
        {
            firstActive = secondActive = -1;
        }

        // Get active IKCyclers
        public (int, int) GetActiveCyclers()
        {
            return (firstActive, secondActive);
        }

        public void SwapActiveCycles()
        {
            var tmp = secondActive;
            secondActive = firstActive;
            firstActive = tmp;
            Synchronise(firstActive);
            Synchronise(secondActive);
        }

        public IKPose GetNextPose(int first, int second, float blendPos)
        {
            if (t > 1.0f || t <= 0.0f)
            {
                t = 0.0f;
            }
            Synchronise(firstActive);
            Synchronise(secondActive);
            cyclers[first].CycleSpeed = speed;
            cyclers[second].CycleSpeed = speed;
            IKPose res = blender.Blend(cyclers[first].GetNextPose(), cyclers[second].GetNextPose(), blendPos);
            t = cyclers[first].AnimationPosition;
            return res;
        }

        public IKPose GetNextPose(int[] cyclerIndecies, float[] blendPositions)
        {
            if (cyclerIndecies.Length < 1 || blendPositions.Length != cyclerIndecies.Length - 1)
            {
                Debug.LogError("IKMultiPoseCycler.GetNextPose(int[], float[]) requires atleast one cycler index and cyclerIndecies.Length - 1 blend positions to work.");
                throw new ArgumentException(string.Format("IKPoseCycler reuires atleast one cycler index and cyclerIndecies.Length - 1 blend positions to work. {0} and {1} given",
                    cyclerIndecies.Length, blendPositions.Length));
            }
            if (t > 1.0f || t <= 0.0f)
            {
                t = 0.0f;
            }
            IKPose res = new IKPose();
            if (cyclerIndecies.Length > parallelThreshold)
            {
                
                Parallel.For(0, cyclerIndecies.Count(), (i, state) =>
                {
                    Synchronise(cyclerIndecies[i]);
                    cyclers[cyclerIndecies[i]].CycleSpeed = speed;
                });
                // Sadly doesn't really benefit from using Job System
                for (int i = 0; i < cyclerIndecies.Length; ++i)
                {
                    res = i == 0 ? cyclers[cyclerIndecies[i]].GetNextPose() : blender.Blend(res, cyclers[cyclerIndecies[i]].GetNextPose(), blendPositions[i - 1]);
                }
            }
            else
            {
                for (int i = 0; i < cyclerIndecies.Length; ++i)
                {
                    Synchronise(cyclerIndecies[i]);
                    cyclers[cyclerIndecies[i]].CycleSpeed = speed;
                    res = i == 0 ? cyclers[cyclerIndecies[i]].GetNextPose() : blender.Blend(res, cyclers[cyclerIndecies[i]].GetNextPose(), blendPositions[i - 1]);
                }
            }
            t = cyclers[cyclerIndecies[0]].AnimationPosition;
            return res;
        }

        public IKPose GetNextPose(float blendPos)
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler.GetNextPose(float) can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            IKPose res = blender.Blend(cyclers[firstActive].GetNextPose(), cyclers[secondActive].GetNextPose(), blendPos);
            t = cyclers[firstActive].AnimationPosition;
            return res;
        }

        public IKPose GetNextPoseWorldSpace(float blendPos, bool set = true)
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler.GetNextPoseWorldSpace(float) can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            IKPose res = blender.BlendWorldSpace(cyclers[firstActive].GetNextPoseWorldSpace(set), cyclers[secondActive].GetNextPoseWorldSpace(set), blendPos);
            t = cyclers[firstActive].AnimationPosition;
            return res;
        }

        public IKPose GetNextPoseLocalSpace(float blendPos, bool set = true)
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler.GetNextPoseWorldSpace(float) can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            IKPose res = blender.BlendLocalSpace(cyclers[firstActive].GetNextPoseLocalSpace(set), cyclers[secondActive].GetNextPoseLocalSpace(set), blendPos);
            t = cyclers[firstActive].AnimationPosition;
            return res;
        }

        public IKPose GetCurrentTargetPose(int index)
        {
            return cyclers[index].GetCurrentTargetPose();
        }

        public IKPose GetCurrentTargetPoseWorldSpace(int index, bool set = true)
        {
            return cyclers[index].GetCurrentTargetPoseWorldSpace(set);
        }

        public IKPose[] GetCurrentTargetPoses()
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler.GetCurrentTargetPoses() can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            return new IKPose[] { cyclers[firstActive].GetCurrentTargetPose(), cyclers[secondActive].GetCurrentTargetPose() };
        }

        public IKPose[] GetCurrentTargetPosesWorldSpace(bool set = true)
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler. GetCurrentTargetPosesWorldSpace() can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            return new IKPose[] { cyclers[firstActive].GetCurrentTargetPoseWorldSpace(set), cyclers[secondActive].GetCurrentTargetPoseWorldSpace(set) };
        }

        public IKPose[] GetCurrentTargetPosesLocalSpace(bool set = true)
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler. GetCurrentTargetPosesWorldSpace() can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            return new IKPose[] { cyclers[firstActive].GetCurrentTargetPoseLocalSpace(set), cyclers[secondActive].GetCurrentTargetPoseLocalSpace(set) };
        }

        // Adjust current target pose just for this cycle only
        public void AdjustCycleTargetPose(int index, IKPose newPose)
        {
            cyclers[index].AdjustCycleTargetPose(newPose);
        }

        // Adjust current target pose for active IKCycler just for this cycle only
        public void AdjustCycleTargetPose(IKPose newPose)
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler.AdjustCycleTargetPose(IKPose) can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            AdjustCycleTargetPose(secondActive, newPose);
        }

        // Adjust current target pose for both active IKCyclers just for this cycle only
        public void AdjustCycleTargetPoses(IKPose newPose1, IKPose newPose2)
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler.AdjustCycleTargetPoses(IKPose[]) can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }

            AdjustCycleTargetPose(firstActive, newPose1);
            AdjustCycleTargetPose(secondActive, newPose2);
        }

        public void SetLocalSpaceTransformer(Transform transform)
        {
            if (cyclers.Count > parallelThreshold)
            {
                Parallel.For(0, cyclers.Count(), (i, state) =>
                {
                    cyclers[i].SetLocalSpaceTransformer(transform);
                });
            }
            else
            {
                for (int k = 0; k < cyclers.Count; ++k)
                {
                    cyclers[k].SetLocalSpaceTransformer(transform);
                }
            }
        }

        public void AdjustCycleSpeed(int first, int second, float newSpeed)
        {
            speed = newSpeed;
            cyclers[first].CycleSpeed = newSpeed;
            cyclers[second].CycleSpeed = newSpeed;
        }

        public void AdjustCycleSpeed(float newSpeed)
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler.AdjustCycleSpeed(float) can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            speed = newSpeed;
            AdjustCycleSpeed(firstActive, secondActive, newSpeed);
        }

        public void SyncCycleSpeeds()
        {
            if (firstActive == -1 || secondActive == -1)
            {
                throw new InvalidOperationException("IKMultiPoseCycler.SyncCycleSpeeds() can only be used after calling IKMultiPoseCycler.SetActive with valid indecies");
            }
            for (int i = 0; i < cyclers.Count; ++i)
            {
                cyclers[i].CycleSpeed = speed;
            }
        }

        public float AnimationPosition
        {
            get { return t; }
            protected set { t = value; }
        }

        public float AnimationSkip()
        {
            if (firstActive == -1 || secondActive == -1)
            {
                if (cyclers.Count <= 5)
                {
                    for (int i = 0; i < cyclers.Count; ++i)
                    {
                        cyclers[i].AnimationSkip();
                    }
                }
                else
                {
                    Parallel.For(0, cyclers.Count(), (i, state) =>
                    {
                        cyclers[i].AnimationSkip();
                    });
                }
                t = cyclers[0].AnimationPosition;
            }
            else
            {
                cyclers[firstActive].AnimationSkip();
                cyclers[secondActive].AnimationSkip();
                t = cyclers[firstActive].AnimationPosition;
            }
            
            return t;
        }

        public void AnimationSkipTo(float position)
        {
            if (firstActive == -1 || secondActive == -1)
            {
                if (cyclers.Count <= 5)
                {
                    for (int i = 0; i < cyclers.Count; ++i)
                    {
                        cyclers[i].AnimationSkipTo(position);
                    }
                }
                else
                {
                    Parallel.For(0, cyclers.Count(), (i, state) =>
                    {
                        cyclers[i].AnimationSkipTo(position);
                    });
                }
            }
            else
            {
                cyclers[firstActive].AnimationSkipTo(position);
                cyclers[secondActive].AnimationSkipTo(position);
            }
            t = position;
        }

        // Sync with another cycler
        public void SyncWith(IKMultiPoseCycler other, float offset = 0.0f)
        {
            float pos = other.t + offset;
            speed = other.speed;
            pos -= Mathf.Floor(pos);
            if (t != pos)
                AnimationSkipTo(pos);
        }
    }

    [Serializable]
    public class IKCyclerMeld : IKMultiPoseCycler
    {
        [SerializeField] int[] active;

        public void ResetCyclePoses(params int[] cycleIndecies)
        {
            if (cycleIndecies.Count() > parallelThreshold)
            {
                Parallel.For(0, cycleIndecies.Count(), (i, state) =>
                {
                    Cyclers[cycleIndecies[i]].ResetCyclePoses();
                });
            }
            else
            {
                for (int i = 0; i < cycleIndecies.Count(); ++i)
                {
                    Cyclers[cycleIndecies[i]].ResetCyclePoses();
                }
            }
        }

        public new void ResetCyclePoses()
        {
            if (active != null)
            {
                ResetCyclePoses(active);
            }
        }

        public void SetActive(params int[] cyclerIndecies)
        {
            if (active is null || active.Count() != cyclerIndecies.Count())
                active = new int[cyclerIndecies.Count()];
            cyclerIndecies.CopyTo(active, 0);
            if (cyclerIndecies.Count() > parallelThreshold)
            {
                Parallel.For(0, cyclerIndecies.Count(), (i, state) =>
                {
                    Synchronise(cyclerIndecies[i]);
                    Cyclers[cyclerIndecies[i]].CycleSpeed = Speed;
                });
            }
            else
            {
                for (int i = 0; i < cyclerIndecies.Count(); ++i)
                {
                    Synchronise(cyclerIndecies[i]);
                    Cyclers[cyclerIndecies[i]].CycleSpeed = Speed;
                }
            }
        }

        public new void SetActive(int first, int second)
        {
            SetActive(new int[] { first, second });
        }

        public new int[] GetActiveCyclers()
        {
            return active;
        }

        public IKPose GetNextPose(int[] cyclerIndecies, float[] blendPositions, bool setActive = true)
        {
            if (cyclerIndecies.Length < 1 || blendPositions.Length != cyclerIndecies.Length - 1)
            {
                Debug.LogError("IKCyclerMeld.GetNextPose(int[], float[]) requires atleast one cycler index and cyclerIndecies.Length - 1 blend positions to work.");
                throw new ArgumentException(string.Format("IKCyclerMeld reuires atleast one cycler index and cyclerIndecies.Length - 1 blend positions to work. {0} and {1} given",
                    cyclerIndecies.Length, blendPositions.Length));
            }
            if (AnimationPosition > 1.0f || AnimationPosition <= 0.0f)
            {
                AnimationPosition = 0.0f;
                ResetCyclePoses();
            }
            IKPose res = new IKPose();
            if (cyclerIndecies.Length > parallelThreshold)
            {
                Parallel.For(0, cyclerIndecies.Count(), (i, state) =>
                {
                    Synchronise(cyclerIndecies[i]);
                    Cyclers[cyclerIndecies[i]].CycleSpeed = Speed;
                });
                // Sadly doesn't really benefit from using Job System
                for (int i = 0; i < cyclerIndecies.Length; ++i)
                {
                    res = i == 0 ? Cyclers[cyclerIndecies[i]].GetNextPose() : Blender.Blend(res, Cyclers[cyclerIndecies[i]].GetNextPose(), blendPositions[i - 1]);
                }
            }
            else
            {
                for (int i = 0; i < cyclerIndecies.Length; ++i)
                {
                    Synchronise(cyclerIndecies[i]);
                    Cyclers[cyclerIndecies[i]].CycleSpeed = Speed;
                    res = i == 0 ? Cyclers[cyclerIndecies[i]].GetNextPose() : Blender.Blend(res, Cyclers[cyclerIndecies[i]].GetNextPose(), blendPositions[i - 1]);
                }
            }
            AnimationPosition = Cyclers[cyclerIndecies[0]].AnimationPosition;
            if (setActive)
                SetActive(cyclerIndecies);
            return res;
        }

        public new IKPose GetNextPose(int[] cyclerIndecies, float[] blendPositions)
        {
            return GetNextPose(cyclerIndecies, blendPositions, false);
        }

        public IKPose GetNextPose(float[] blendPos)
        {
            if (active is null || active.Count() != blendPos.Count())
            {
                throw new InvalidOperationException("IKMultiPoseCycler.GetNextPose(float) can only be used after calling IKMultiPoseCycler.SetActive with valid indecies; the number of blend positions must match the number of active indecies");
            }
            return GetNextPose(active, blendPos);
        }

        public new IKPose GetNextPose(float blendPosition)
        {
            if (active is null || active.Length < 1)
            {
                throw new InvalidOperationException("IKCyclerMeld.GetNextPose(float) can only be used after calling IKCyclerMeld.SetActive with valid indecies");
            }
            if (AnimationPosition > 1.0f || AnimationPosition <= 0.0f)
            {
                AnimationPosition = 0.0f;
                ResetCyclePoses();
            }
            IKPose res = new IKPose();
            // Sadly doesn't really benefit from using Job System
            for (int i = 0; i < active.Length; ++i)
            {
                res = i == 0 ? Cyclers[active[i]].GetNextPose() : Blender.Blend(res, Cyclers[active[i]].GetNextPose(), blendPosition);
            }
            AnimationPosition = Cyclers[active[0]].AnimationPosition;
            return res;
        }

        public new IKPose GetNextPoseWorldSpace(float blendPos, bool set = true)
        {
            if (active is null || active.Length < 1)
            {
                throw new InvalidOperationException("IKCyclerMeld.GetNextPose(float) can only be used after calling IKCyclerMeld.SetActive with valid indecies");
            }
            if (AnimationPosition > 1.0f || AnimationPosition <= 0.0f)
            {
                AnimationPosition = 0.0f;
                ResetCyclePoses();
            }
            IKPose res = new IKPose();
            // Sadly doesn't really benefit from using Job System
            for (int i = 0; i < active.Length; ++i)
            {
                res = i == 0 ? Cyclers[active[i]].GetNextPoseWorldSpace(set) : Blender.BlendWorldSpace(res, Cyclers[active[i]].GetNextPoseWorldSpace(set), blendPos);
            }
            AnimationPosition = Cyclers[active[0]].AnimationPosition;
            return res;
        }

        public new IKPose GetNextPoseLocalSpace(float blendPos, bool set = true)
        {
            if (active is null || active.Length < 1)
            {
                throw new InvalidOperationException("IKCyclerMeld.GetNextPose(float) can only be used after calling IKCyclerMeld.SetActive with valid indecies");
            }
            if (AnimationPosition > 1.0f || AnimationPosition <= 0.0f)
            {
                AnimationPosition = 0.0f;
                ResetCyclePoses();
            }
            IKPose res = new IKPose();
            // Sadly doesn't really benefit from using Job System
            for (int i = 0; i < active.Length; ++i)
            {
                res = i == 0 ? Cyclers[active[i]].GetNextPoseLocalSpace(set) : Blender.BlendLocalSpace(res, Cyclers[active[i]].GetNextPoseLocalSpace(set), blendPos);
            }
            AnimationPosition = Cyclers[active[0]].AnimationPosition;
            return res;
        }

        public void AdjustCycleTargetPoses(params IKPose[] adjustedTargets)
        {
            if (active is null || active.Length != adjustedTargets.Length)
            {
                throw new InvalidOperationException("IKCyclerMeld.GetNextPose(float) can only be used after calling IKCyclerMeld.SetActive with valid indecies");
            }
            if (active.Length > parallelThreshold)
            {
                Parallel.For(0, active.Count(), (i, state) =>
                {
                    Cyclers[active[i]].AdjustCycleTargetPose(adjustedTargets[i]);
                });
            }
            else
            {
                for (int i = 0; i < active.Length; ++i)
                {
                    Cyclers[active[i]].AdjustCycleTargetPose(adjustedTargets[i]);
                }
            }
        }

        public new float AnimationSkip()
        {
            if (active is null || active.Length == 0)
            {
                if (Cyclers.Count > parallelThreshold)
                {
                    Parallel.For(0, Cyclers.Count(), (i, state) =>
                    {
                        Cyclers[i].AnimationSkip();
                    });
                }
                else 
                {
                    for (int i = 0; i < Cyclers.Count; ++i)
                    {
                        Cyclers[i].AnimationSkip();
                    }
                }
                
                AnimationPosition = Cyclers[0].AnimationPosition;
            }
            else
            {
                if (active.Length > parallelThreshold)
                {
                    Parallel.For(0, active.Length, (i, state) =>
                    {
                        Cyclers[active[i]].AnimationSkip();
                    });
                }
                else
                {
                    for (int i = 0; i < active.Length; ++i)
                    {
                        Cyclers[active[i]].AnimationSkip();
                    }
                }
                
                AnimationPosition = Cyclers[active[0]].AnimationPosition;
            }
            return AnimationPosition;
        }

        public new void AnimationSkipTo(float position)
        {
            if (active is null || active.Length == 0)
            {
                if (Cyclers.Count > parallelThreshold)
                {
                    for (int i = 0; i < Cyclers.Count; ++i)
                    {
                        Cyclers[i].AnimationSkipTo(position);
                    }
                }
                else
                {
                    Parallel.For(0, Cyclers.Count(), (i, state) =>
                    {
                        Cyclers[i].AnimationSkipTo(position);
                    });
                }
            }
            else
            {
                if (active.Length > parallelThreshold)
                {
                    Parallel.For(0, active.Length, (i, state) =>
                    {
                        Cyclers[active[i]].AnimationSkipTo(position);
                    });
                }
                else
                {
                    for (int i = 0; i < active.Length; ++i)
                    {
                        Cyclers[active[i]].AnimationSkipTo(position);
                    }
                }
            }
            AnimationPosition = position;
        }

        public void SyncWith(IKCyclerMeld other, float offset = 0.0f)
        {
            float pos = other.AnimationPosition + offset;
            Speed = other.Speed;
            pos -= Mathf.Floor(pos);
            if (AnimationPosition != pos)
                AnimationSkipTo(pos);
        }

    }
}