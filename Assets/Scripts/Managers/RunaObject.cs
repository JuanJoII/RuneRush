using System.Collections;
using UnityEngine;

public class RunaObject : MonoBehaviour
{
    public string RunaId = "";
    public string ObjectType = "runa_comun";  // "runa_comun" | "powerup_viento"

    [SerializeField] private float rotSpeed = 80f;
    [SerializeField] private float bobHeight = 0.2f;
    [SerializeField] private float bobSpeed = 2.2f;

    private Vector3 _basePos;

    private void Start()
    {
        _basePos = transform.position;
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Update()
    {
        transform.Rotate(Vector3.up, rotSpeed * Time.deltaTime);
        float y = _basePos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(_basePos.x, y, _basePos.z);
    }
}

    
