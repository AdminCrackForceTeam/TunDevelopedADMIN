using UnityEngine;

public class UltimateHeadLockAim : MonoBehaviour
{
    [Header("=== BASE AIM SETTINGS ===")]
    public float baseSensitivity = 100f;
    public float dynamicBoost = 50f;
    public float maxSensitivity = 300f;
    public AnimationCurve responseCurve;       // Curve cho sensitivity dynamic
    public float microDeadZone = 0.05f;
    public float horizontalBias = 1f;

    [Header("=== HEAD LOCK CORE ===")]
    public float headZonePercent = 0.70f;      // Vị trí pitch head (0.65~0.75 tùy game)
    public float snapUpTrigger = 0.42f;        // Kéo lên % này → snap lock ngay
    public float unlockDownTrigger = -0.58f;   // Phải kéo xuống mạnh thế này để unlock
    public float minLockTime = 0.18f;          // Lock ít nhất bao lâu (chống unlock nhầm)
    public int unlockHoldFramesNeeded = 9;     // Giữ kéo xuống bao nhiêu frame (~0.15s @60fps)
    public float unlockCooldown = 0.12f;       // Sau unlock chờ bao lâu mới lock lại
    public float autoLockRange = 9f;           // Gần head zone + kéo lên → tự lock

    [Header("=== YAW SOFT MAGNET (Assist khi lock) ===")]
    public Transform cameraTransform;          // Camera để tính hướng nhìn
    public LayerMask enemyLayer;
    public float yawMagnetStrength = 1.2f;     // 0.8~2.0 → mạnh nhẹ yaw hút
    public float yawMagnetFOV = 60f;           // Góc FOV tìm enemy cho magnet
    public float maxYawCorrectionSpeed = 180f; // Max độ/giây yaw điều chỉnh

    [Header("=== TRUE AIMBOT MODE (Toggle) ===")]
    public bool aimbotActive = false;          // Toggle bằng nút (ví dụ Key L)
    public float aimbotFOV = 45f;
    public float aimbotSnapSpeed = 12f;        // Tốc độ snap yaw/pitch
    public float aimbotRange = 80f;

    // Private vars
    private bool headLocked = false;
    private float currentPitch = 0f;
    private float lockCooldown = 0f;
    private float minLockTimer = 0f;
    private int unlockHoldFrames = 0;
    private Transform currentTargetHead = null;
    private Transform lockedEnemyHead = null;

    public void Aim(Vector2 joy)
    {
        // Deadzone + sensitivity
        joy.x = Mathf.Abs(joy.x) < microDeadZone ? 0f : joy.x;
        joy.y = Mathf.Abs(joy.y) < microDeadZone ? 0f : joy.y;

        float mag = Mathf.Clamp01(joy.magnitude);
        float curve = responseCurve.Evaluate(mag);
        float sens = Mathf.Min(baseSensitivity + curve * dynamicBoost, maxSensitivity);

        float deltaX = joy.x * curve * sens * Time.unscaledDeltaTime * 0.01f * horizontalBias;
        float deltaY = joy.y * curve * sens * Time.unscaledDeltaTime * 5f;

        float HEAD_ZONE = maxPitch * headZonePercent; // maxPitch cần define ở đâu đó (ví dụ public float maxPitch = 85f;)

        // Update cooldown
        lockCooldown = Mathf.Max(0f, lockCooldown - Time.unscaledDeltaTime);

        // ────────────────────────────────────────────────────────────────
        // AIMBOT MODE (nếu bật) → override hết bằng raycast snap
        // ────────────────────────────────────────────────────────────────
        if (aimbotActive)
        {
            if (Input.GetKeyDown(KeyCode.L)) // Hoặc button joystick toggle
                aimbotActive = false;

            if (lockedEnemyHead == null || !IsTargetVisible(lockedEnemyHead))
                lockedEnemyHead = FindBestEnemyHead();

            if (lockedEnemyHead != null)
            {
                headLocked = true;

                Vector3 dirToHead = lockedEnemyHead.position - cameraTransform.position;
                float targetPitch = -Mathf.Asin(dirToHead.normalized.y) * Mathf.Rad2Deg;
                float targetYaw = Mathf.Atan2(dirToHead.x, dirToHead.z) * Mathf.Rad2Deg;

                // Smooth snap
                currentPitch = Mathf.LerpAngle(currentPitch, targetPitch, aimbotSnapSpeed * Time.unscaledDeltaTime);

                float currentYaw = GetCurrentYaw();
                float newYaw = Mathf.LerpAngle(currentYaw, targetYaw, aimbotSnapSpeed * Time.unscaledDeltaTime);
                deltaX = Mathf.DeltaAngle(currentYaw, newYaw); // override yaw
            }
        }
        else
        {
            // ────────────────────────────────────────────────────────────────
            // NORMAL HEAD LOCK + SOFT YAW MAGNET
            // ────────────────────────────────────────────────────────────────
            float freePitch = currentPitch - deltaY;

            if (!headLocked)
            {
                bool shouldSnap = false;

                // Snap mạnh
                if (joy.y >= snapUpTrigger && lockCooldown <= 0f)
                    shouldSnap = true;

                // Auto-catch gần head
                if (Mathf.Abs(freePitch - HEAD_ZONE) < autoLockRange && joy.y > 0.35f && lockCooldown <= 0f)
                    shouldSnap = true;

                if (shouldSnap)
                {
                    currentPitch = HEAD_ZONE;
                    headLocked = true;
                    minLockTimer = minLockTime;
                    unlockHoldFrames = 0;
                }
                else
                {
                    currentPitch = freePitch;
                }
            }
            else
            {
                currentPitch = HEAD_ZONE;
                minLockTimer -= Time.unscaledDeltaTime;

                // Soft Yaw Magnet
                if (currentTargetHead == null || !IsTargetVisible(currentTargetHead))
                    currentTargetHead = FindNearestHeadInView();

                if (currentTargetHead != null && Mathf.Abs(joy.x) < 0.4f) // Không kéo ngang mạnh
                {
                    Vector3 dirToHead = (currentTargetHead.position - cameraTransform.position).normalized;
                    Vector3 flatDir = new Vector3(dirToHead.x, 0f, dirToHead.z).normalized;

                    float targetYaw = Mathf.Atan2(flatDir.x, flatDir.z) * Mathf.Rad2Deg;
                    float currentYaw = GetCurrentYaw();

                    float deltaYaw = Mathf.DeltaAngle(currentYaw, targetYaw);
                    float correction = deltaYaw * yawMagnetStrength * Time.unscaledDeltaTime;
                    correction = Mathf.Clamp(correction, -maxYawCorrectionSpeed * Time.unscaledDeltaTime, maxYawCorrectionSpeed * Time.unscaledDeltaTime);

                    deltaX += correction;
                }

                // Unlock logic
                if (joy.y <= unlockDownTrigger)
                {
                    unlockHoldFrames++;
                    if (unlockHoldFrames >= unlockHoldFramesNeeded && minLockTimer <= 0f)
                    {
                        headLocked = false;
                        unlockHoldFrames = 0;
                        lockCooldown = unlockCooldown;
                        currentPitch -= deltaY * 0.28f; // thoát mượt
                    }
                }
                else
                {
                    unlockHoldFrames = 0;
                }
            }
        }

        // Clamp & Apply
        currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch); // minPitch/maxPitch cần define

        transform.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);

        ApplyYaw(deltaX);

        pitch = currentPitch; // nếu code khác dùng biến pitch
    }

    // Helpers
    private float GetCurrentYaw()
    {
        return transform.parent != null ? transform.parent.eulerAngles.y : transform.eulerAngles.y;
    }

    private void ApplyYaw(float deltaYaw)
    {
        if (transform.parent != null)
            transform.parent.Rotate(Vector3.up * deltaYaw);
        else
            transform.Rotate(Vector3.up * deltaYaw, Space.World);
    }

    private Transform FindNearestHeadInView()
    {
        Collider[] hits = Physics.OverlapSphere(cameraTransform.position, 50f, enemyLayer);
        Transform best = null;
        float bestDot = 0.3f;

        foreach (var col in hits)
        {
            Transform head = col.transform.Find("Head") ?? col.transform;
            Vector3 dir = (head.position - cameraTransform.position).normalized;
            float dot = Vector3.Dot(cameraTransform.forward, dir);

            if (dot > bestDot && dot > Mathf.Cos(yawMagnetFOV * 0.5f * Mathf.Deg2Rad))
            {
                bestDot = dot;
                best = head;
            }
        }
        return best;
    }

    private Transform FindBestEnemyHead()
    {
        Collider[] hits = Physics.OverlapSphere(cameraTransform.position, aimbotRange, enemyLayer);
        Transform best = null;
        float bestScore = 0f;

        foreach (var col in hits)
        {
            Transform head = col.transform.Find("Head") ?? col.transform;
            Vector3 dir = (head.position - cameraTransform.position).normalized;
            float dist = Vector3.Distance(cameraTransform.position, head.position);
            float dot = Vector3.Dot(cameraTransform.forward, dir);

            if (dot > Mathf.Cos(aimbotFOV * 0.5f * Mathf.Deg2Rad))
            {
                float score = dot * (1f / (dist + 1f));
                if (score > bestScore && IsTargetVisible(head))
                {
                    bestScore = score;
                    best = head;
                }
            }
        }
        return best;
    }

    private bool IsTargetVisible(Transform target)
    {
        if (target == null) return false;
        Vector3 dir = target.position - cameraTransform.position;
        return !Physics.Raycast(cameraTransform.position, dir.normalized, dir.magnitude);
        // Nếu muốn check chính xác hơn: return hit.transform == target || hit.transform.IsChildOf(target);
    }

    // Toggle aimbot ví dụ (gọi từ Update hoặc Input System)
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.L))
        {
            aimbotActive = !aimbotActive;
            if (!aimbotActive)
            {
                lockedEnemyHead = null;
                headLocked = false;
            }
        }
    }
}
