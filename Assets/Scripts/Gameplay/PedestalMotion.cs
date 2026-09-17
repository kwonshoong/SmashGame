using UnityEngine;

namespace SmashGame
{
    /// <summary>
    /// 움직이는 받침대 (레퍼런스 375레벨 참고): 제자리 회전(턴테이블)·상하 왕복(승강)·둘 다.
    /// 받침대 묶음(상판+기둥)에 kinematic Rigidbody를 두고 MovePosition/MoveRotation으로 움직이므로 위에 놓인 블록은
    /// 접촉 마찰로 같이 실려 간다. 시작 후 startDelay 동안은 정지 — 플레이어가 구조물을 먼저 보게.
    /// </summary>
    public class PedestalMotion : MonoBehaviour
    {
        public float spinDegPerSec;   // 0이면 회전 없음
        public float bobAmplitude;    // 0이면 승강 없음 (월드 단위)
        public float bobPeriod = 4f;
        public float phase;           // 승강 위상(라디안) — 받침대마다 다르게 주면 번갈아 오르내린다
        public float startDelay = 1.0f;
        /// <summary>실려 있는 블록들. 잠든 블록은 kinematic 받침대가 움직여도 깨어나지 않아(실측) 주기적으로 깨운다.</summary>
        public System.Collections.Generic.List<Block> blocks;
        int step;
        float angle;
        public const float SpinRampSeconds = 2f;

        Rigidbody rb;
        Vector3 basePos;
        Quaternion baseRot;   // 돌려 놓은 상판(yaw)의 시작 회전. 회전 운동은 여기에 더한다
        float t;

        public static PedestalMotion Attach(GameObject group, float spin, float bobAmp, float bobPeriod, float phase)
        {
            var rb = group.GetComponent<Rigidbody>();
            if (rb == null) rb = group.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var m = group.AddComponent<PedestalMotion>();
            m.spinDegPerSec = spin; m.bobAmplitude = bobAmp; m.bobPeriod = Mathf.Max(0.5f, bobPeriod); m.phase = phase;
            return m;
        }

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            basePos = transform.position;
            baseRot = transform.rotation;
        }

        void FixedUpdate()
        {
            if (rb == null) return;
            t += Time.fixedDeltaTime;
            float u = Mathf.Max(0f, t - startDelay);
            if (u > 0f && blocks != null)
            {
                // 움직이는 동안 블록이 잠들면 안 된다: 회전 속도가 느려 안쪽 블록만 잠들었다가 20스텝 뒤 깨어나면 그 사이 받침대가
                // 돌아간 만큼 이웃과 어긋나 탑이 무너졌다(실측). 잠듦 문턱을 0으로 두고 매 스텝 깨운다.
                foreach (var b in blocks)
                    if (b != null && !b.Removed)
                    {
                        var brb = b.GetComponent<Rigidbody>();
                        if (brb == null) continue;
                        if (brb.sleepThreshold != 0f) brb.sleepThreshold = 0f;
                        if (brb.IsSleeping()) brb.WakeUp();
                    }
            }
            // 승강: 원래 높이를 중심으로 ±amplitude 대칭 왕복. (1-cos) 형태를 쓰면 위로만 오르내려 테이블이 평균 +0.35 높아 보였다.
            // 처음 2초는 램프로 진폭을 키워 급출발 없이 시작한다.
            float ramp = Mathf.SmoothStep(0f, 1f, u / 2f);
            float y = bobAmplitude > 0f ? bobAmplitude * ramp * Mathf.Sin(phase + u * Mathf.PI * 2f / bobPeriod) : 0f;
            rb.MovePosition(basePos + Vector3.up * y);
            if (spinDegPerSec != 0f)
            {
                // 회전 속도는 2초에 걸쳐 부드럽게 올린다. 갑자기 돌기 시작하면 바닥 블록만 먼저 끌려가 높은 탑이 전단으로 무너진다(실측).
                float w = spinDegPerSec * Mathf.SmoothStep(0f, 1f, u / SpinRampSeconds);
                angle += w * Time.fixedDeltaTime;
                // 절대 회전을 쓰면 yaw로 돌려 놓은 상판이 첫 스텝에 0°로 튀어 블록만 남기고 빠져나간다(대각선 벽 L138 실측) → 시작 회전에 더한다
                rb.MoveRotation(Quaternion.Euler(0f, angle, 0f) * baseRot);
            }
        }
    }
}
