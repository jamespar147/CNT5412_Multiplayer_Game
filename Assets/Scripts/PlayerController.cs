using System;
using UnityEngine;

public class PlayerController : MonoBehaviour {

    public GameObject bulletPrefab;
    public Transform bulletSpawn;
    public Camera cam;
    public Rigidbody body;
    public Collider playerCollider;
    public AudioListener playerAudio;
    public AudioSource pewSource;
    public AudioClip pew;

    public bool isLocalPlayer = false;
    public float JumpSpeed = 50000.0f;
    private float distToGround;

    Vector3 oldPosition;
    Vector3 currentPosition;
    Quaternion oldRotation;
    Quaternion currentRotation;

	// Use this for initialization
	void Start () {
        oldPosition = transform.position;
        currentPosition = oldPosition;
        oldRotation = transform.rotation;
        currentRotation = oldRotation;

        playerCollider = GetComponentInParent<Collider>();
        playerAudio = GetComponentInParent<AudioListener>();

        if (!isLocalPlayer) {
            cam.enabled = false;
            playerAudio.enabled = false;
        }

        if (isLocalPlayer)
            distToGround = playerCollider.bounds.extents.y;
    }

    // Update is called once per frame
    void Update () {
	    if (!isLocalPlayer) {
            return;
        }

        var x = Input.GetAxis("Mouse X") * Time.deltaTime * 150.0f;
        var z = Input.GetAxis("Vertical") * Time.deltaTime * 15.0f;
        var y = Input.GetAxis("Horizontal") * Time.deltaTime * 15.0f;

        if (Input.GetKeyDown("space") && IsGrounded()) {
            body.AddForce(Vector3.up * JumpSpeed);
        }

        transform.Rotate(0, x, 0);
        transform.Translate(y, 0, z);

        currentPosition = transform.position;
        currentRotation = transform.rotation;

        if (currentPosition != oldPosition) {
            NetworkManager.instance.GetComponent<NetworkManager>().CommandMove(transform.position);
            oldPosition = currentPosition;
        }

        if (currentRotation != oldRotation) {
            NetworkManager.instance.GetComponent<NetworkManager>().CommandTurn(transform.rotation);
            oldRotation = currentRotation;
        }

        if (Input.GetMouseButtonDown(0)) {
            NetworkManager networkManager = NetworkManager.instance.GetComponent<NetworkManager>();
            networkManager.CommandShoot();
        }
    }

    public void CmdFire() {
        pewSource.PlayOneShot(pew, 1.0f);
        var bullet = Instantiate(bulletPrefab, bulletSpawn.position, bulletSpawn.rotation) as GameObject;

        Bullet b = bullet.GetComponent<Bullet>();
        b.playerFrom = this.gameObject;
        bullet.GetComponent<Rigidbody>().velocity = bullet.transform.up * 40;

        Destroy(bullet, 2.0f);
    }

    public bool IsGrounded() {
        return Physics.Raycast(transform.position, -Vector3.up, distToGround + 0.1f);
    }
}
