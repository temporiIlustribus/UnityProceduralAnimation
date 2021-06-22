using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class PlayerManager : MonoBehaviour
{
    public InputMaster inputMaster;
    public RigidBodyPlayerControll playerControll;
    public float health;
    public ProceduralAnimation.PlayerAnimationAlternative playerAnimation;
    public bool useNetworkMLAPI;
    void Awake()
    {
        playerControll.UseNetworkMLAPI = useNetworkMLAPI;
        if (inputMaster == null)
        {
            inputMaster = new InputMaster();
        }
    }
    public void DecreaseHealth(float amount)
    {
        if (health > 0.0f)
        {
            health -= amount;
        }
        else
        {
            // Loose state
            Debug.Log("Health reached 0");
        }
    }
}
