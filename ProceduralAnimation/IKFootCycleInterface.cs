using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralAnimation
{
    // The StepParams is used primarily to make it easy to have variable number, and type arguments as
    // Step function input. Theres almost no restrictions for what StepParams can be.
    public interface IIKFootSolver<StepParams> where StepParams : new()
    {
        void Step(StepParams stepParams);

        int ComparePositions(Vector3 target, RaycastHit lhs, RaycastHit rhs);
        bool isMoving();
        List<RaycastHit> LocalSearchPoints { get; }
        Transform Body { get; }
        Vector3 InitialPos { get; }
        Vector3 EffectorPosition { get; }
        Vector3 EffectorNormal { get; }
        Vector3 TargetPosition { get; }
        Vector3 TargetNormal { get; }
        Vector3 DesiredPos { get; }
        Vector3 BarrierTopPos { get; }
        Vector3 BarrierHit { get; }
        LayerMask TerrainLayer { get; }
    }
}
