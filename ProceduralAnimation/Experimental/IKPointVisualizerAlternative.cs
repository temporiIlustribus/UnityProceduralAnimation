using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralAnimation
{
    [RequireComponent(typeof(IKFootSolver))]
    public class IKPointVisualizerAlternative : GenericIKPointVisualizer<IKFootSolverAlternative, (IKMultiPoseCycler, float, float, bool)> { };
}