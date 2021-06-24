## Procedural Animation System
---


This is a custom implementation of a unique approach to procedural animation, which combines the strengths of two different existing solutions - David Rosen's Overgrowth and Ubisofts IK Rig technology. This system uses Unity's Animation Rigging package. 

### Abstract
The implementation is divided into two primary functional layers (due to the fact that Animation Rigging does not work correctly with Unity's physics engine) and a layer of abstraction, which joins them together (which requires specific implementation for you project and provides easy to understand interface for high-level logic). It is specifically designed to address the needs of indie game developers and small teams, as the curve-based approach to animation simplifies the process of creating animation sequences and cycles, the usage of IK allows for a lot of flexibility and behavioral complexity, and the FK active ragdoll provides  physically accurate environment interaction. 

### How does it work?
The basic approach is to use `IK Pose`s (defined by IK effector positions) and only use the necessary key-poses, interpolating between them using user-defined curves for any intermediate pose. The IK layer, informed by surrounding environment, will provide the basic motion, which is closely replicated by the active ragdoll, which provides physics-interaction.
For each `IK Pose` it is possible to define the type of adjustment (or warping) that is applicable for it. For example, a `Sticky` pose is a pose that is meant to "stick" to a certain coordinate in world space. This is useful for eliminating foot-sliding and any interactions with the environment. Other adjustment types like `Repel` and `Attract`, are useful for stepping over small obstacles and reaching out to specific gameobjects respectively.

#### What about GUI and user-experience?
Currently the system does not have a custom GUI for the Editor. This will be fixed in future revisions. 
Please Note that this is not a plug-and-play solution for your projects (yet). This is a system implementation (plus a working usage example).

### What classes should I care about for my project?
The main classes (adn their related interfaces), which you can use for procedural animation are `IKPose`, `IKPoseAnaimtion`, `FKLimb` and `FKLayer`. 
Example of a system which you can build using these classes can be found in `PlayerAnimationAlternative`, `IKFootSolverAlternative` - which show how this system can be used to create procedural leg animation. 

#### Small demo:
Stair Demonstration for leg IK

<img src="https://media.giphy.com/media/wrBeu9fc6venDspj0A/giphy-downsized-large.gif" width="480">
