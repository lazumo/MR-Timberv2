using UnityEngine;

public class SimpleMover : MonoBehaviour
{
    public float speed = 2f;

    void Update()
    {
        float x = Input.GetAxis("Horizontal"); // A / D
        float z = Input.GetAxis("Vertical");   // W / S

        transform.Translate(new Vector3(x, 0, z) * speed * Time.deltaTime);

        if (Input.GetKey(KeyCode.Q))
            transform.Rotate(Vector3.up, -60f * Time.deltaTime);

        if (Input.GetKey(KeyCode.E))
            transform.Rotate(Vector3.up, 60f * Time.deltaTime);
    }
}
