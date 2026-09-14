using UnityEngine;

namespace SmashGame
{
    /// <summary>진자 망치: 위에서 매달려 좌우로 흔들리며 공을 막고 블록을 스친다.</summary>
    public class PendulumHammer : MonoBehaviour
    {
        public float amplitude = 55f;
        public float speed = 1.1f;
        Rigidbody rb;
        float phase;

        public static PendulumHammer Create(Transform parent, Vector3 pivot, float armLength)
        {
            var root = new GameObject("PendulumHammer");
            root.transform.SetParent(parent);
            root.transform.position = pivot;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var arm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            arm.name = "Arm";
            arm.transform.SetParent(root.transform, false);
            arm.transform.localPosition = new Vector3(0, -armLength * 0.5f, 0);
            arm.transform.localScale = new Vector3(0.12f, armLength * 0.5f, 0.12f);
            arm.GetComponent<Renderer>().material = Materials.Get(new Color(1f, 0.75f, 0.2f), false, true);

            var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, -armLength, 0);
            head.transform.localRotation = Quaternion.Euler(0, 0, 90);
            head.transform.localScale = new Vector3(0.7f, 0.6f, 0.7f);
            head.GetComponent<Renderer>().material = Materials.Get(new Color(0.55f, 0.2f, 0.7f), true);

            var p = root.AddComponent<PendulumHammer>();
            p.rb = rb;
            p.phase = Random.Range(0f, 6f);
            return p;
        }

        void FixedUpdate()
        {
            float a = amplitude * Mathf.Sin((Time.time + phase) * speed);
            rb.MoveRotation(Quaternion.Euler(0, 0, a));
        }
    }

    /// <summary>회전 풍차: 구조물 앞에서 3개 날개가 천천히 돌며 공을 막는다.</summary>
    public class Windmill : MonoBehaviour
    {
        public float rpm = 12f;
        Rigidbody rb;

        public static Windmill Create(Transform parent, Vector3 center, float bladeLength)
        {
            var root = new GameObject("Windmill");
            root.transform.SetParent(parent);
            root.transform.position = center;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = "Hub";
            hub.transform.SetParent(root.transform, false);
            hub.transform.localRotation = Quaternion.Euler(90, 0, 0);
            hub.transform.localScale = new Vector3(0.6f, 0.12f, 0.6f);
            hub.GetComponent<Renderer>().material = Materials.Get(new Color(1f, 0.8f, 0.2f), false, true);

            for (int i = 0; i < 3; i++)
            {
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blade.name = "Blade" + i;
                blade.transform.SetParent(root.transform, false);
                float ang = i * 120f;
                blade.transform.localRotation = Quaternion.Euler(0, 0, ang);
                blade.transform.localPosition = blade.transform.localRotation * new Vector3(0, bladeLength * 0.5f, 0);
                blade.transform.localScale = new Vector3(0.22f, bladeLength, 0.18f);
                blade.GetComponent<Renderer>().material = Materials.Get(i % 2 == 0 ? new Color(1f, 0.8f, 0.2f) : new Color(0.55f, 0.2f, 0.7f));
            }

            var w = root.AddComponent<Windmill>();
            w.rb = rb;
            return w;
        }

        void FixedUpdate()
        {
            rb.MoveRotation(Quaternion.Euler(0, 0, Time.time * rpm * 6f));
        }
    }
}
