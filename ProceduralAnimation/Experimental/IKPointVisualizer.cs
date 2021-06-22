using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProceduralAnimation
{
    [RequireComponent(typeof(IKFootSolver))]
    public class IKPointVisualizer : GenericIKPointVisualizer<IKFootSolver, bool> { };
}