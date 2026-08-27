// Storyloom Unity Kit — camera follow (smooth, top-down).
using UnityEngine;
namespace Storyloom
{
    public class SimpleFollow : MonoBehaviour
    {
        public Transform target; public float smooth = 8f; public Vector3 offset = new Vector3(0, 0, -10);
        void LateUpdate() { if (!target) return; transform.position = Vector3.Lerp(transform.position, target.position + offset, Time.deltaTime * smooth); }
    }
}
