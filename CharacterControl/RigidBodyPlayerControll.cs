using System.Collections;
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using System.Collections.Specialized;
using MLAPI;


public class RigidBodyPlayerControll : NetworkBehaviour, InputMaster.IPlayerActions
{
    
    private Rigidbody playerRigidbody;
    private CapsuleCollider playerCollider;
    public Transform playerTransform;
    public Transform cameraTransform;
    public PlayerManager playerManager;

    [SerializeField] bool useNetworkMLAPI = false;

    // Mostly here for convinience corresponds to moving 1kg
    [SerializeField] private float forceModifier = 1000;
    [SerializeField] [Range(0, 1)] private float minMove = 0.09f;
    [SerializeField] [Range(0, 1)] private float crouchSizeModifier = 0.5f;
    [SerializeField] private float crouchShrinkSpeed = 4f;

    // Drag settings for the character to not be sliding all over the place
    [SerializeField] private float groundDrag = 1.15f;
    [SerializeField] private float airСontrol = 0.25f;
    [SerializeField] private float airDrag = 1.028f;

    // Basic speed parameters (meters / second)
    [SerializeField] private float speed = 2.25f; // slightly faster than average walk speed
    [SerializeField] private float jumpSpeed = 21f;
    [SerializeField] private float runSpeedModifier = 2f;
    [SerializeField] private float airRunModifier = 1.3f; // corresponds to a run speed of about 4.8 m/s
    [SerializeField] private float crouchSpeedModifier = 0.6f;
    [SerializeField] private float airCrouchSpeedModifier = 0.85f;

    // Look related parameters
    [SerializeField] private float mouseSensetivity = 14; // Should be set in settings
    [SerializeField] [Range(0, 1)] private float cameraSmoothing = 0.25f;

    // For how longg can the player run and how long it takes to recover from running
    [SerializeField] [Range(0, 10)] private float runCapacity = 3f;
    [SerializeField] [Range(0, 1)] private float runRechargeAmount = 0.004f;
    [SerializeField] [Range(0, 1)] private float crouchRunRechargeAmount = 0.006f;

    // Ground detection
    [SerializeField] private uint jumpCooldown = 40u;
    [SerializeField] private Transform groundCheckPos;
    [SerializeField] [Range(0, 1)] private float groundCheckDistance = 0.38f;
    [SerializeField] private int layerMask = (1 << 9) + (1 << 1) + 1;
    [SerializeField] private float supportClearDelay = 1.1f;
    private float xRotation = 0;

    private float x;
    private float z;

    private float mouseX;
    private float mouseY;

    private float verticalSpeed = 0;
    public float curRunCapacity = 0;
    public uint curJumpCapacity = 0;
    public uint groundCheckUpdDelay = 8;
    // Debug params
    public bool isGrounded = true;
    private bool jumping = false;
    public bool sprinting = false;
    public bool crouching = false;


    private float crouchAnimationTime = 0;
    private uint updCounter = 0;

    private float smoothingCoef = 1;

    [SerializeField] private bool rechargingRun = false;

    public bool UseNetworkMLAPI
    {
        get { return useNetworkMLAPI; }
        set { useNetworkMLAPI = value; }
    }

    public void Awake()
    {
        // Tell the "gameplay" action map that we want to get notified
        // when actions get triggered.
        if (playerManager.inputMaster == null)
            playerManager.inputMaster = new InputMaster();
        playerManager.inputMaster.Player.SetCallbacks(this);
    }


    // Start is called before the first frame update
    void Start()
    {
        Camera cam = gameObject.GetComponentInChildren<Camera>();
        if (useNetworkMLAPI)
        {
            if (!IsLocalPlayer)
            {
                cam.gameObject.SetActive(false);
            }
        }

        curRunCapacity = runCapacity;
        playerRigidbody = this.GetComponent<Rigidbody>();
        playerCollider = this.GetComponent<CapsuleCollider>();
        smoothingCoef = Mathf.Lerp(1 / Time.fixedDeltaTime, 1f, cameraSmoothing);
        playerManager.playerAnimation.UpdateMotionData(playerRigidbody, this.transform, isGrounded);
    }

    // Fixed update Physics based controlls
    private void FixedUpdate()
    {
        if (useNetworkMLAPI)
        {
            if (!IsLocalPlayer)
            {
                return; // Update is for local players ONLY
            }
        }
        /*
         * Current approach: 
         *     Get the projection of horizontal plane (x,z) velocity on to current look
         *     Clear all velocity
         *     Add velocity multiplied by a drag parameter (y axis treated separately)
         *   
         * Note that Ground Check computation only runs only on a fraction of Fixed Updates. (For performance reasons).
         */
        if (updCounter >= groundCheckUpdDelay)
        {
            updCounter = 0;
            isGrounded = GroundCheck(groundCheckPos.position);
        }

        // This code is only needed to eliminate "ghost sliding", caused by small scale sliding
        if (playerRigidbody.velocity.magnitude < minMove && isGrounded)
            playerRigidbody.Sleep();


        // Update current look direction according to mouse movement
        // We do not preserve angular velocity or use force-defined acceleration
        // this would make mouse control imprecise, inacurate and "weighty"
        UpdatePlayerLookDirection();

        // Update MotionData for procedural player animation
        playerManager.playerAnimation.UpdateMotionData(playerRigidbody, this.transform, isGrounded);


        // Get velocity projection of character velocity vector on the new look direction plane
        Vector3 movement = playerTransform.right * x + playerTransform.forward * z;
        Vector3 velProj = LookVelocityProjection();

        // Reset Rigidbody state
        playerRigidbody.velocity.Set(0, 0, 0); // killing velocity is not enough
        playerRigidbody.Sleep(); // Sleep to reset acceleration


        if (!isGrounded)
        {
            movement *= airСontrol;
            velProj.Scale(new Vector3(1 / airDrag, 1, 1 / airDrag));
        }
        else
        {
            velProj.Scale(new Vector3(1 / groundDrag, 1, 1 / groundDrag));
        }

        if (crouching)
        {
            if (playerTransform.localScale.y > crouchSizeModifier)
                playerTransform.localScale = new Vector3(1, calcPlayerScale(false), 1);
            if (isGrounded)
                movement *= crouchSpeedModifier;
            else
                movement *= airCrouchSpeedModifier;
        }
        else if (playerTransform.localScale.y < 1.0f)
        {
            playerTransform.localScale = new Vector3(1, calcPlayerScale(true), 1);
        }

        if (sprinting && curRunCapacity >= 0 && movement.magnitude >= minMove)
        {
            //Debug.Log("Sprinting");
            if (isGrounded)
                movement *= runSpeedModifier;
            else
                movement *= airRunModifier;
            curRunCapacity -= Time.deltaTime;
            if (curRunCapacity < 0)
            {
                sprinting = false;
                curJumpCapacity = 0;
            }
        }

        if ((!sprinting || movement.magnitude < minMove) && curRunCapacity < runCapacity)
        {
            if (curRunCapacity <= 0)
            {
                curRunCapacity = 0;
                //Debug.Log("Will be able to run in " + (runCapacity * runCapacity * Time.deltaTime / runRechargeAmount));
                if (!rechargingRun)
                {
                    if (!crouching)
                        StartCoroutine(RunRecharge((runCapacity / runRechargeAmount) * Time.deltaTime));
                    else
                        StartCoroutine(RunRecharge((runCapacity / runRechargeAmount) * Time.deltaTime));
                }
            }
            else
            {
                if (!crouching)
                    curRunCapacity += runRechargeAmount;
                else
                    curRunCapacity += crouchRunRechargeAmount;
                if (curRunCapacity > runCapacity)
                    curRunCapacity = runCapacity;
            }
        }

        // Update current jump capacity

        UpdateJumpCapacity();

        // If we want to jump and are standing - jump
        if (jumping && isGrounded)
        {
            
            //Rigidbody pickupRB = playerManager.pickupRaycasting.getPickupRB();
            //if (playerManager.pickupRaycasting.supportObj == playerManager.pickupRaycasting.pickedObject)
            //{
            //    playerManager.pickupRaycasting.dropItem(true);
            //     StartCoroutine(SupportClear(supportClearDelay, playerManager.pickupRaycasting.supportObj));
            //    if (pickupRB != null)
            //        pickupRB.AddForce(new Vector3(0, -verticalSpeed * forceModifier * Time.deltaTime, 0), ForceMode.Impulse);
            //}
            
            isGrounded = false;
            curJumpCapacity = 0;
            verticalSpeed = jumpSpeed;
            movement.y = verticalSpeed / speed;
            
            //if (pickupRB != null)
            //{
            //    movement.y -= Mathf.Log10(Mathf.Max(pickupRB.mass + 0.1f, 1.0f));
            //}
            
            //Debug.Log(Time.time);
        }

        playerRigidbody.AddForce(velProj, ForceMode.VelocityChange); // use velocityChange mode for exponential velocity falloff
        playerRigidbody.AddForce(movement * forceModifier * speed, ForceMode.Impulse);
        //playerManager.playerAnimation.UpdateMotionData(playerRigidbody, this.transform, isGrounded);
        ++updCounter;
    }

    private void UpdatePlayerLookDirection()
    {
        // Mouse look

        mouseX *= mouseSensetivity * Time.deltaTime;
        mouseY *= mouseSensetivity * Time.deltaTime;

        // To avoid confusion:
        // xRotation and yRotation referse to the axis of rotation
        // This mean that the xRotation corresponds to "vertical" camera pitch
        // and the yRotation refers to the direction the character is currently facing
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -89f, 89f);

        // Update player rotation 

        Quaternion newCameraRotation = Quaternion.Euler(xRotation, 0, 0);
        Quaternion newPlayerRotation = Quaternion.Euler(playerTransform.eulerAngles.x, playerTransform.eulerAngles.y + mouseX, playerTransform.eulerAngles.z);

        cameraTransform.localRotation = Quaternion.Slerp(cameraTransform.localRotation, newCameraRotation, Time.deltaTime * smoothingCoef);
        playerTransform.localRotation = Quaternion.Slerp(playerTransform.localRotation, newPlayerRotation, Time.deltaTime * smoothingCoef);
    }

    private Vector3 LookVelocityProjection() {
        Vector2 horizontalVelocity = new Vector2(playerRigidbody.velocity.x, playerRigidbody.velocity.z);
        Vector2 forwardPlane = new Vector2(playerTransform.forward.normalized.x, playerTransform.forward.normalized.z);
        Vector2 rightPlane = new Vector2(playerTransform.right.normalized.x, playerTransform.right.normalized.z);
        Vector2 projection = forwardPlane * Vector2.Dot(forwardPlane, horizontalVelocity) + rightPlane * Vector2.Dot(rightPlane, horizontalVelocity);
        return new Vector3(projection.x, playerRigidbody.velocity.y, projection.y);
    }

    private float calcPlayerScale(bool direction) {
        float newChScale = crouchSizeModifier;
        if (!direction)
        {
            if (crouchAnimationTime <= 1.0f)
            {
                newChScale = Mathf.Lerp(playerTransform.localScale.y, crouchSizeModifier, crouchAnimationTime * (playerTransform.localScale.y - crouchSizeModifier) / (1 - crouchSizeModifier));
                crouchAnimationTime += crouchShrinkSpeed * Time.deltaTime;
            }
        }
        else
        {
            newChScale = 1.0f;
            if (crouchAnimationTime <= 1.0f)
            {
                newChScale = Mathf.Lerp(playerTransform.localScale.y, 1.0f, crouchAnimationTime * (1 - playerTransform.localScale.y) / (1 - crouchSizeModifier));
                crouchAnimationTime += crouchShrinkSpeed * Time.deltaTime;
            }
        }
        return newChScale;
    }

    private bool GroundColCheck(Collider col) {
        //return (col.gameObject == playerManager.pickupRaycasting.pickedObject);
        return false;
    }

    private bool GroundCheck(Vector3 position)
    {
        Collider[] temp = Physics.OverlapSphere(position, groundCheckDistance, layerMask);
        if (temp.Length > 0)
        {
            //playerManager.pickupRaycasting.supportObj = temp[0].gameObject;
            bool colFlag = false;
            for (uint i = 0; i < temp.Length; ++i)
            {
                if (GroundColCheck(temp[i]))
                {
                    //playerManager.pickupRaycasting.supportObj = temp[i].gameObject;
                    //playerManager.pickupRaycasting.dropItem(true);
                }
                else
                {
                    colFlag = true;
                }
            }
            return colFlag;
        }
        else
        {
            //StartCoroutine(SupportClear(supportClearDelay, playerManager.pickupRaycasting.supportObj));
        }
        return isGrounded;
    }


    IEnumerator SupportClear(float time, GameObject obj)
    {
        if (obj != null)
        {
            yield return new WaitForSeconds(time);
            //if (playerManager.pickupRaycasting.supportObj == obj)
            //    playerManager.pickupRaycasting.supportObj = null;
        }

    }

    IEnumerator RunRecharge(float time)
    {
        if (rechargingRun)
            yield break;
        rechargingRun = true;
        yield return new WaitForSeconds(time);
        curRunCapacity = runCapacity;
        rechargingRun = false;
    }

    private uint UpdateJumpCapacity()
    {
        if (curJumpCapacity < jumpCooldown)
            ++curJumpCapacity;
        return curJumpCapacity;
    }

    void OnEnable()
    {
        playerManager.inputMaster.Player.Enable();
    }

    void OnDisable()
    {
        playerManager.inputMaster.Player.Disable();
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        Vector2 dir = context.ReadValue<Vector2>();
        x = dir.x;
        z = dir.y;
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        Vector2 dir = context.ReadValue<Vector2>();
        mouseX = dir.x;
        mouseY = dir.y;
    }

    public void OnJumpStart(InputAction.CallbackContext context)
    {
        jumping = false;
        if (curJumpCapacity == jumpCooldown)
            jumping = true;
    }

    public void OnJumpCancel(InputAction.CallbackContext context)
    {
        jumping = false;
    }

    public void OnSprintStart(InputAction.CallbackContext context)
    {
        sprinting = false;
        if (!crouching)
            sprinting = true;
    }

    public void OnSprintCancel(InputAction.CallbackContext context)
    {
        sprinting = false;
    }

    public void OnCrouchStart(InputAction.CallbackContext context)
    {
        crouchAnimationTime = 0;
        sprinting = false;
        crouching = true;

    }

    public void OnCrouchCancel(InputAction.CallbackContext context)
    {
        crouchAnimationTime = 0;
        crouching = false;
    }

    public void OnInterractStart(InputAction.CallbackContext context)
    {
        //playerManager.pickupRaycasting.InterractStart();
        Debug.Log("Start");
    }

    public void OnInterractCancel(InputAction.CallbackContext context)
    {
        //playerManager.pickupRaycasting.InterractCancel();
        Debug.Log("Cancel");
    }

    public void OnThrowStart(InputAction.CallbackContext context)
    {
        //playerManager.pickupRaycasting.ThrowStart();
    }

    public void OnThrowCancel(InputAction.CallbackContext context)
    {
        //playerManager.pickupRaycasting.ThrowCancel();
    }

    public void OnChangeColorFilter(InputAction.CallbackContext context) {
        //playerManager.colorFilter.ChangeFilter();
    }

    public void OnEscape(InputAction.CallbackContext context) {
        Debug.Log("Escaped");

    }
}
