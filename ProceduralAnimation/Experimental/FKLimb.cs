using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class FKLimb
{
    public GameObject PlayerObj;
    public PlayerManager playerManager;
    public ConfigurableJoint joint;
    public float minimumForce;

    public void ScaleJointForce(float value)
    {
        var drive = joint.angularXDrive;
        drive.positionSpring *= value;
        if (drive.positionSpring < minimumForce)
            drive.positionSpring = minimumForce;
        joint.angularXDrive = drive;
        drive = joint.angularYZDrive;
        drive.positionSpring *= value;
        if (drive.positionSpring < minimumForce)
            drive.positionSpring = minimumForce;
        joint.angularYZDrive = drive;
    }
    public void SetJointForce(float value)
    {
        var drive = joint.angularXDrive;
        drive.positionSpring = Mathf.Max(value, minimumForce);
        joint.angularXDrive = drive;
        drive = joint.angularYZDrive;
        drive.positionSpring = Mathf.Max(value, minimumForce);
        joint.angularYZDrive = drive;
    }
}
