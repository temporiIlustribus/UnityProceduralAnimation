using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

interface IPose<T> where T : new()
{
    Vector3 Position
    {
        get;
        set;
    }
    Quaternion Rotation
    {
        get;
        set;
    }
    void ConvertToLocalSpace(Transform transform, bool cacheTransformer);
    void ConvertToWorldSpace(Transform transform, bool cacheTransformer);
    T Blend(T other, float positionBlend, float rotationBlend);
    T Blend(T other, float factor);
    T BlendLocal(T other, float positionBlend, float rotationBlend);
    T BlendLocal(T other, float factor);
    T BlendWorld(T other, float positionBlend, float rotationBlend);
    T BlendWorld(T other, float factor);
    Transform Apply(Transform transform);
}


[Serializable]
public struct IKPose : IPose<IKPose>
{

    /* 
     * Useful for defining whether a specific pose should be sticky (for example foor sticking to ground)
     * Attract to a target (for example move towards a door handle)
     * Or repel from something (for stepping over a small barrier) 
     */
    public enum AdjustmentType { Any, Stick, Attract, Repel }

    [SerializeField] Vector3 position;
    [SerializeField] Quaternion rotation;
    [SerializeField] AdjustmentType adjustmentType;
    [SerializeField] bool isLocal;
    bool validated;
    Transform localTransformer;
    /*
     * Each type has a perceived "strength". When deriving a pose Adjustment type the strength never increases
     * "Strengths":
     *  0 - Any
     *  1 - Attract, Repel
     *  2 - Stick
     */

    private AdjustmentType SolveDerivedAdjustment(AdjustmentType other, float posBlend, float rotBlend)
    {
        if (posBlend <= Mathf.Epsilon && rotBlend <= Mathf.Epsilon)
            return adjustmentType;
        else if (posBlend >= 1 - Mathf.Epsilon && rotBlend >= 1 - Mathf.Epsilon)
            return other;
        if (adjustmentType == AdjustmentType.Any || other == AdjustmentType.Any)
            return AdjustmentType.Any;
        if (adjustmentType == other)
            return adjustmentType;
        // if one is Attract and the other is Repel then there is no obvious solution other than Any
        return adjustmentType == AdjustmentType.Stick ? other : other == AdjustmentType.Stick ? adjustmentType : AdjustmentType.Any;
    }

    private void ValidateRotation()
    {
        if (Mathf.Abs(rotation.x) <= Mathf.Epsilon && Mathf.Abs(rotation.y) <= Mathf.Epsilon
            && Mathf.Abs(rotation.z) <= Mathf.Epsilon && Mathf.Abs(rotation.w) <= Mathf.Epsilon)
        {
            rotation = Quaternion.identity;
        }
        validated = true;
    }

    public IKPose(IKPose other)
    {
        position = other.position;
        rotation = other.rotation;
        validated = other.validated;
        adjustmentType = other.adjustmentType;
        isLocal = other.isLocal;
        localTransformer = other.localTransformer;
        validated = other.validated;
    }

    public IKPose(Vector3 pos, Quaternion rot, AdjustmentType adj = AdjustmentType.Any, bool usesLocalSpace = true, Transform transformer = null)
    {
        position = pos;
        rotation = rot;
        validated = false;
        adjustmentType = adj;
        isLocal = usesLocalSpace;
        localTransformer = transformer;
        ValidateRotation();
    }

    public IKPose(Vector3 pos, bool usesLocalSpace = true)
    {
        position = pos;
        rotation = Quaternion.identity;
        validated = true;
        isLocal = usesLocalSpace;
        adjustmentType = AdjustmentType.Any;
        localTransformer = null;
    }

    public IKPose(Quaternion rot, bool usesLocalSpace = true)
    {
        position = Vector3.zero;
        rotation = rot;
        validated = false;
        adjustmentType = AdjustmentType.Any;
        isLocal = usesLocalSpace;
        localTransformer = null;
        ValidateRotation();
    }

    // Get or set internal position with world position (internaly the pose may store this position in world space or in local space)
    public Vector3 WorldPos
    {
        get
        {
            if (!isLocal)
                return position;
            if (localTransformer != null)
                return localTransformer.parent.TransformPoint(position);
            throw new InvalidOperationException(string.Format("Unable to get world position for IKPose {0} (which is maintained in local space) without space transformers", this));
        }
        set
        {
            if (isLocal && localTransformer != null)
                position = localTransformer.parent.InverseTransformPoint(value);
            else if (isLocal && localTransformer == null)
                throw new InvalidOperationException(string.Format("Unable to set world position for IKPose {0} (which is maintained in local space) without space transformers", this));
            else if (!isLocal)
                position = value;
        }
    }

    // Get or set internal position with local position (internaly the pose may store this position in world space or in local space)
    public Vector3 LocalPos
    {
        get
        {
            if (isLocal)
                return position;
            if (localTransformer != null)
                return localTransformer.parent.InverseTransformPoint(position);
            throw new InvalidOperationException(string.Format("Unable to get local position for IKPose {0} (which is maintained in world space) without space transformers", this));
        }
        set
        {
            if (!isLocal && localTransformer != null)
                position = localTransformer.parent.TransformPoint(value);
            else if (!isLocal && localTransformer == null)
                throw new InvalidOperationException(string.Format("Unable to set local position for IKPose {0} (which is maintained in world space) without space transformers", this));
            else if (isLocal)
                position = value;
        }
    }

    // Get or set internal rotation with world space rotation (internaly the pose may store this rotation in world space or in local space)
    public Quaternion WorldRot
    {
        get
        {
            if (!isLocal)
                return rotation;
            if (localTransformer != null)
                return localTransformer.parent.rotation * rotation;
            throw new InvalidOperationException(string.Format("Unable to get world rotation for IKPose {0} (which is maintained in local space) without space transformers", this));
        }
        set
        {
            if (isLocal && localTransformer != null)
                rotation = Quaternion.Inverse(localTransformer.parent.rotation) * value;
            else if (isLocal && localTransformer == null)
                throw new InvalidOperationException(string.Format("Unable to set world rotation for IKPose {0} (which is maintained in local space) without space transformers", this));
            else if (!isLocal)
                rotation = value;
            ValidateRotation();
        }
    }

    // Get or set internal rotation with local space rotation (internaly the pose may store this rotation in world space or in local space)
    public Quaternion LocalRot
    {
        get
        {
            if (isLocal)
                return rotation;
            if (localTransformer != null)
                return Quaternion.Inverse(localTransformer.parent.rotation) * rotation;
            throw new InvalidOperationException(string.Format("Unable to get local rotation for IKPose {0} (which is maintained in world space) without space transformers", this));
        }
        set
        {
            if (!isLocal && localTransformer != null)
                rotation = localTransformer.parent.rotation * value;
            else if (!isLocal && localTransformer == null)
                throw new InvalidOperationException(string.Format("Unable to set local rotation for IKPose {0} (which is maintained in world space) without space transformers", this));
            else if (isLocal)
                rotation = value;
            ValidateRotation();
        }
    }

    // Forces pose to be saved in local space
    public void ConvertToLocalSpace(Transform transform, bool cacheTransformer = true)
    {
        if (cacheTransformer)
            localTransformer = transform;
        if (!isLocal)
        {
            position = transform.parent.InverseTransformPoint(position);
            // Inverse parent rotation to get a worldSpace Quaternion. Apply the new local rotation
            rotation = Quaternion.Inverse(transform.parent.rotation) * rotation;
            ValidateRotation();
            isLocal = true;
        }
    }

    // Forces pose to be saved in world space
    public void ConvertToWorldSpace(Transform transform, bool cacheTransformer = true)
    {
        if (cacheTransformer)
            localTransformer = transform;
        if (isLocal)
        {
            position = transform.parent.TransformPoint(position);
            // Inverse parent rotation to get a worldSpace Quaternion. Apply the new local rotation
            rotation = transform.parent.rotation * rotation;
            ValidateRotation();
            isLocal = false;
        }
    }

    public void ConvertToLocalSpace()
    {
        ConvertToLocalSpace(localTransformer, false);
    }

    public void ConvertToWorldSpace()
    {
        ConvertToWorldSpace(localTransformer, false);
    }

    public IKPose GetWorldSpace(Transform transform, bool cacheTransformer = true)
    {
        if (cacheTransformer)
            localTransformer = transform;
        if (isLocal)
        {
            IKPose pose = new IKPose(this);
            pose.localTransformer = transform;
            pose.ConvertToWorldSpace();
            return pose;
        }
        return this;
    }

    public IKPose GetWorldSpace()
    {
        return GetWorldSpace(localTransformer, false);
    }

    public IKPose GetLocalSpace(Transform transform, bool cacheTransformer = true)
    {
        if (cacheTransformer)
            localTransformer = transform;
        if (!isLocal)
        {
            IKPose pose = new IKPose(this);
            pose.localTransformer = transform;
            pose.ConvertToLocalSpace();
            return pose;
        }
        return this;
    }

    public IKPose GetLocalSpace()
    {
        return GetLocalSpace(localTransformer, false);
    }

    public Transform LocalSpaceTransformer
    {
        get { return localTransformer; }
        set { localTransformer = value; }
    }

    // Access internal position (which may or may no be set in local space)
    public Vector3 Position
    {
        get { return position; }
        set { position = value; }
    }

    // Access internal rotation (which may or may no be set in local space)
    public Quaternion Rotation
    {
        get { return rotation; }
        set
        {
            rotation = value;
            ValidateRotation();
        }
    }

    // Check whether pose information is maintained in world space or in local space
    public bool isLocalSpace
    {
        get { return isLocal; }
        set
        {
            if (value != isLocal)
            {
                if (value)
                    ConvertToLocalSpace();
                else
                    ConvertToWorldSpace();
            }
        }
    }

    public Vector3 BlendPosition(IKPose other, float factor)
    {
        if (isLocal == other.isLocal)
            return Vector3.Lerp(position, other.position, factor);
        if (!isLocal && other.isLocal)
        {
            if (other.localTransformer != null)
                return Vector3.Lerp(position, other.WorldPos, factor);
            else if (localTransformer != null)
                return Vector3.Lerp(LocalPos, other.position, factor);
            
            throw new InvalidOperationException(string.Format("Unable to blend IKPoses where {0} is {1} and {2} is {3} without space transformers", 
                                                this, this.isLocalSpace ? "local space" : "world space", other, other.isLocalSpace ? "local space" : "world space"));
        }
        // isLocal && !other.isLocal
        if (localTransformer != null)
            return Vector3.Lerp(WorldPos, other.position, factor);
        else if (other.localTransformer != null)
            return Vector3.Lerp(position, other.LocalPos, factor);
        throw new InvalidOperationException(string.Format("Unable to blend IKPoses where {0} is {1} and {2} is {3} without space transformers", 
                                            this, this.isLocalSpace ? "local space" : "world space", other, other.isLocalSpace ? "local space" : "world space"));

    }

    public Quaternion BlendRotation(IKPose other, float factor)
    {
        if (!validated)
        {
            ValidateRotation();
        }
        if (isLocal == other.isLocal)
            return Quaternion.Lerp(rotation, other.rotation, factor);
        if (!isLocal && other.isLocal)
        {
            if (other.localTransformer != null)
                return Quaternion.Lerp(rotation, other.WorldRot, factor);
            else if (localTransformer != null)
                return Quaternion.Lerp(LocalRot, other.rotation, factor);
            throw new InvalidOperationException(string.Format("Unable to blend IKPoses where {0} is {1} and {2} is {3} without space transformers",
                                                this, this.isLocalSpace ? "local space" : "world space", other, other.isLocalSpace ? "local space" : "world space"));
        }
        // isLocal && !other.isLocal
        if (localTransformer != null)
            return Quaternion.Lerp(WorldRot, other.rotation, factor);
        else if (other.localTransformer != null)
            return Quaternion.Lerp(rotation, other.LocalRot, factor);
        throw new InvalidOperationException(string.Format("Unable to blend IKPoses where {0} is {1} and {2} is {3} without space transformers", 
                                            this, this.isLocalSpace ? "local space" : "world space", other, other.isLocalSpace ? "local space" : "world space"));
    }

    public IKPose Blend(IKPose other, float positionBlend, float rotationBlend)
    {
        if (!validated)
        {
            ValidateRotation();
        }
        if (isLocal == other.isLocal)
            return new IKPose(Vector3.Lerp(position, other.position, positionBlend), Quaternion.Lerp(rotation, other.rotation, rotationBlend), 
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), isLocal, localTransformer);
        if (!isLocal && other.isLocal)
        {
            if (other.localTransformer != null)
                return new IKPose(Vector3.Lerp(position, other.WorldPos, positionBlend), Quaternion.Lerp(rotation, other.WorldRot, rotationBlend), 
                                        SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), false, other.localTransformer);
            else if (localTransformer != null)
                return new IKPose(Vector3.Lerp(LocalPos, other.position, positionBlend), Quaternion.Lerp(LocalRot, other.rotation, rotationBlend),
                                        SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), true, localTransformer);
            throw new InvalidOperationException(string.Format("Unable to blend IKPoses where {0} is {1} and {2} is {3} without space transformers", 
                                                this, this.isLocalSpace ? "local space" : "world space", other, other.isLocalSpace ? "local space" : "world space"));
        }
        // isLocal && !other.isLocal
        if (localTransformer != null)
            return new IKPose(Vector3.Lerp(WorldPos, other.position, positionBlend), Quaternion.Lerp(WorldRot, other.rotation, rotationBlend),
                                         SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), false, localTransformer);
        else if (other.localTransformer != null)
            return new IKPose(Vector3.Lerp(position, other.LocalPos, positionBlend), Quaternion.Lerp(rotation, other.LocalRot, rotationBlend),
                                         SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), true, other.localTransformer);
        throw new InvalidOperationException(string.Format("Unable to blend IKPoses where {0} is {1} and {2} is {3} without space transformers", 
                                            this, this.isLocalSpace ? "local space" : "world space", other, other.isLocalSpace ? "local space" : "world space"));
    }

    public IKPose Blend(IKPose other, float factor)
    {
        return Blend(other, factor, factor);
    }

    // Force blend in Local space (if possible)
    public IKPose BlendLocal(IKPose other, float positionBlend, float rotationBlend)
    {
        if (!validated)
        {
            ValidateRotation();
        }
        if (isLocal && other.isLocal)
            return new IKPose(Vector3.Lerp(position, other.position, positionBlend), Quaternion.Lerp(rotation, other.rotation, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), isLocal, localTransformer);
        if (isLocal && !other.isLocal)
        {
            if (other.localTransformer != null)
                return new IKPose(Vector3.Lerp(position, other.LocalPos, positionBlend), Quaternion.Lerp(rotation, other.LocalRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), true, other.localTransformer);
            else if (localTransformer != null)
            {
                IKPose otherLocal = other.GetLocalSpace(this.localTransformer, false);
                return new IKPose(Vector3.Lerp(position, otherLocal.LocalPos, positionBlend), Quaternion.Lerp(rotation, otherLocal.LocalRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), true, localTransformer);
            }
        }
        if (!isLocal && localTransformer != null)
        {
            if (other.isLocal)
                return new IKPose(Vector3.Lerp(LocalPos, other.position, positionBlend), Quaternion.Lerp(LocalRot, other.rotation, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), true, localTransformer);
            else if (other.localTransformer != null)
                return new IKPose(Vector3.Lerp(LocalPos, other.LocalPos, positionBlend), Quaternion.Lerp(LocalRot, other.LocalRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), true, localTransformer);
            IKPose otherLocal = other.GetLocalSpace(this.localTransformer, false);
            return new IKPose(Vector3.Lerp(LocalPos, otherLocal.LocalPos, positionBlend), Quaternion.Lerp(LocalRot, otherLocal.LocalRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), true, localTransformer);
        }
        else if (!isLocal && localTransformer == null && other.localTransformer != null)
        {
            IKPose thisLocal = this.GetLocalSpace(other.localTransformer, false);
            return new IKPose(Vector3.Lerp(thisLocal.LocalPos, other.LocalPos, positionBlend), Quaternion.Lerp(thisLocal.LocalRot, other.LocalRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), true, other.localTransformer);
        }
        throw new InvalidOperationException(string.Format("Unable to blend IKPoses where {0} is {1} and {2} is {3} without space transformers", 
                                            this, this.isLocalSpace ? "local space" : "world space", other, other.isLocalSpace ? "local space" : "world space"));
    }

    public IKPose BlendLocal(IKPose other, float factor)
    {
        return BlendLocal(other, factor, factor);
    }

    // Force blend in World space (if possible)
    public IKPose BlendWorld(IKPose other, float positionBlend, float rotationBlend)
    {
        if (!validated)
        {
            ValidateRotation();
        }
        if (!isLocal && !other.isLocal)
            return new IKPose(Vector3.Lerp(position, other.position, positionBlend), Quaternion.Lerp(rotation, other.rotation, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), isLocal, localTransformer);
        if (!isLocal && other.isLocal)
        {
            if (other.localTransformer != null)
                return new IKPose(Vector3.Lerp(position, other.WorldPos, positionBlend), Quaternion.Lerp(rotation, other.WorldRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), false, other.localTransformer);
            else if (localTransformer != null)
            {
                IKPose otherWorld = other.GetWorldSpace(this.localTransformer, false);
                return new IKPose(Vector3.Lerp(position, otherWorld.WorldPos, positionBlend), Quaternion.Lerp(rotation, otherWorld.WorldRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), false, localTransformer);
            }
        }
        if (isLocal && localTransformer != null)
        {
            if (!other.isLocal)
                return new IKPose(Vector3.Lerp(WorldPos, other.position, positionBlend), Quaternion.Lerp(WorldRot, other.rotation, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), false, localTransformer);
            else if (other.localTransformer != null)
                return new IKPose(Vector3.Lerp(WorldPos, other.WorldPos, positionBlend), Quaternion.Lerp(WorldRot, other.WorldRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), false, localTransformer);
            IKPose otherWorld = other.GetWorldSpace(this.localTransformer, false);
            return new IKPose(Vector3.Lerp(WorldPos, otherWorld.WorldPos, positionBlend), Quaternion.Lerp(WorldRot, otherWorld.WorldRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), false, localTransformer);
        }
        else if (isLocal && localTransformer == null && other.localTransformer != null)
        {
            IKPose thisWorld = this.GetWorldSpace(other.localTransformer, false);
            return new IKPose(Vector3.Lerp(thisWorld.WorldPos, other.WorldPos, positionBlend), Quaternion.Lerp(thisWorld.WorldRot, other.WorldRot, rotationBlend),
                                            SolveDerivedAdjustment(other.adjustmentType, positionBlend, rotationBlend), false, other.localTransformer);
        }
        throw new InvalidOperationException(string.Format("Unable to blend IKPoses where {0} is {1} and {2} is {3} without space transformers", this, this.isLocalSpace ? "local space" : "world space", other, other.isLocalSpace ? "local space" : "world space"));
    }

    public IKPose BlendWorld(IKPose other, float factor)
    {
        return BlendWorld(other, factor, factor);
    }

    // Apply Pose to a transform
    public Transform Apply(Transform transform)
    {
        if (isLocal)
        {
            transform.localRotation = LocalRot;
            transform.localPosition = LocalPos;
        }
        else
        {
            transform.rotation = WorldRot;
            transform.position = WorldPos;
        }
        return transform;
    }

    public AdjustmentType AdjustType
    {
        get { return adjustmentType; }
        set { adjustmentType = value; }
    }

}