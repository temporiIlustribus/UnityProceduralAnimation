using System.Collections.Specialized;
using UnityEngine;
using System.Collections;

public class PickupRaycasting : MonoBehaviour
{
    
    public GameObject pickedObject;
    public PlayerManager playerManager;

    [SerializeField] private Transform target;
    [SerializeField] private float pickUpRange;
    [SerializeField] private float rayCastRange;
    [SerializeField] private float holdForce;
    [SerializeField] private float camRepelForce;
    [SerializeField] private float throwForce;
    [SerializeField] private float maxVelocity;
    [SerializeField] private bool lookingAtPickupable = false;
    public bool isHoldingObject = false;
    
    [SerializeField] private BitVector32 layerMask = new BitVector32(1 << 9);

    private Collider curPickuableCollider;
    private Rigidbody curPickupableRigidbody;
    private Vector3 targetForceVector;
    private Vector3 camToTargetVector;

    public float pickUpTimeoutTime = 0.8f;
    public bool pickUpTimeout = false;

    // Character controller pass-through
    private bool doInterract = false;
    private bool doThrow = false;
    public GameObject supportObj = null;

    [SerializeField] private float accumThrowCharge = 0f;
    [SerializeField] private float throwChargeUnit = 1f;
    [SerializeField] private float maxThrowForce = 1000f;

    public void InterractStart()
    {
        doInterract = true;
    }

    public void InterractCancel()
    {
        doInterract = false;
    }

    public void ThrowStart()
    {
        doThrow = true;
        Debug.Log("Throw started");
    }

    public void ThrowCancel()
    {
        doThrow = false;
        Debug.Log("Throw cancelled");
        if (isHoldingObject && curPickupableRigidbody != null)
        {
            pickUpTimeout = true;
            StartCoroutine(PickupRecharge(pickUpTimeoutTime));
            curPickupableRigidbody.AddForce((curPickupableRigidbody.worldCenterOfMass - this.transform.position) * maxThrowForce * accumThrowCharge, ForceMode.Impulse);
            curPickupableRigidbody.useGravity = true;
            isHoldingObject = false;
            curPickuableCollider = null;
            curPickupableRigidbody = null;
            pickedObject = null;
        }
        accumThrowCharge = 0.0f;
    }

    // Start is called before the first frame update
    void Start()
    {
        camToTargetVector = this.transform.position - target.position;
    }
    
    public void dropItem(bool groundFlag) 
    {
        if (groundFlag)
        {
            pickUpTimeout = true;
            StartCoroutine(PickupRecharge(pickUpTimeoutTime));
        }
        if (curPickupableRigidbody != null)
            curPickupableRigidbody.useGravity = true;
        isHoldingObject = false;
        curPickuableCollider = null;
        curPickupableRigidbody = null;
        pickedObject = null;
    }

    IEnumerator PickupRecharge(float time)
    {
        yield return new WaitForSeconds(time);
        pickUpTimeout = false;
    }

    private void pickUpItem(GameObject obj) 
    {
        if (!pickUpTimeout)
        {
            curPickuableCollider = obj.GetComponent<Collider>();
            curPickupableRigidbody = obj.GetComponent<Rigidbody>();
            curPickupableRigidbody.useGravity = false;
            curPickupableRigidbody.velocity = Vector3.zero;
            curPickupableRigidbody.angularVelocity.Set(0.0f, 0.0f, 0.0f);
            isHoldingObject = true;
            curPickupableRigidbody.Sleep();
        }
        
    }
    
    private bool rayCastCheck() 
    {
        Ray ray = new Ray(this.transform.position, transform.forward);
        RaycastHit hit;

        Physics.Raycast(ray, out hit, rayCastRange, layerMask.Data);
        
        if (hit.transform != null && hit.transform.gameObject != supportObj && hit.transform.gameObject.GetComponent<Rigidbody>() != null)
        {
            pickedObject = hit.transform.gameObject;
            if (Vector3.Distance(target.transform.position, pickedObject.GetComponent<Rigidbody>().worldCenterOfMass) <= pickUpRange)
            {
                //Debug.Log("In pickup range");
                return true;
            }
        }
        return false;
    }

    private void Update()
    {
        if (isHoldingObject && curPickupableRigidbody == null)
        {
            isHoldingObject = false;
            curPickuableCollider = null;
            pickedObject = null;
        }
        //playerManager.crosshairController.changeCrosshair(isHoldingObject);
    }

    private void FixedUpdate()
    {       
        if (!isHoldingObject && doInterract && !pickUpTimeout) 
        {
            if (rayCastCheck()) 
            {
                pickUpItem(pickedObject);
            
            }
        }
        
        if (isHoldingObject && curPickupableRigidbody != null) 
        {
            if (doThrow && accumThrowCharge < 1.0f)
                accumThrowCharge += Mathf.Min(1.0f - accumThrowCharge, throwChargeUnit * Time.deltaTime);

            if (!doInterract || Vector3.Distance(curPickupableRigidbody.worldCenterOfMass, target.position) > pickUpRange || 
                                curPickupableRigidbody.velocity.magnitude > maxVelocity)
            {
                curPickupableRigidbody.velocity *= 0.5f;
                dropItem(false);
            } 
            else 
            {
                // Clear any velocity left on the object
                curPickupableRigidbody.velocity = new Vector3(0.0f, 0.0f, 0.0f);
                curPickupableRigidbody.angularVelocity.Set(0.0f, 0.0f, 0.0f);
                curPickupableRigidbody.Sleep();
                
                camToTargetVector =  target.position - this.transform.position;
                camToTargetVector = new Vector3(camToTargetVector.x , 0, camToTargetVector.z);
                
                
                targetForceVector = (target.position - curPickupableRigidbody.worldCenterOfMass);

                targetForceVector = new Vector3(Mathf.Sign(targetForceVector.x) * targetForceVector.x * targetForceVector.x, 
                                                Mathf.Sign(targetForceVector.y) * targetForceVector.y * targetForceVector.y, 
                                                Mathf.Sign(targetForceVector.z) * targetForceVector.z * targetForceVector.z);

                // Apply force towards target

                Vector3 resForce = targetForceVector * holdForce;
                float pickupToCamDist = Vector3.Distance(this.transform.position, curPickupableRigidbody.worldCenterOfMass);
                if (pickupToCamDist < 1.51f)
                    resForce += camToTargetVector * camRepelForce * (1.51f - pickupToCamDist);
                if (curPickupableRigidbody.mass >= 1)
                    curPickupableRigidbody.AddForce(resForce, ForceMode.Force);
                else
                    curPickupableRigidbody.AddForce(resForce * curPickupableRigidbody.mass, ForceMode.Force);
            }
        }
    }

    public Rigidbody getPickupRB()
    {
        if (isHoldingObject)
            return curPickupableRigidbody;
        return null;
    }
}
