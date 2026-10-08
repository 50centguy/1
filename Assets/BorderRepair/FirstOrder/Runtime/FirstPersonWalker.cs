using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BorderRepair.FirstOrder
{
    /// <summary>
    /// 第一人称行走（CharacterController）：
    /// - WASD / 方向键移动，Shift 快走，C 切换蹲下（按住 Ctrl 也蹲），鼠标看；
    /// - 鼠标锁在画面中央（准星）时左键操作、右键观察准星对着的东西（交给 FirstOrderInput）；
    /// - Tab 或 Esc 放开鼠标去点界面；再点一下画面（不在界面上）重新锁住；
    /// - 有界面需要鼠标时（<see cref="CursorRequests"/>：展开的手册、夜末面板等）自动放开鼠标、停止移动和转头；
    /// - 镜头在固定机位（“近看”）时：本组件不动，按移动键回到行走。
    /// 只碰非触发碰撞体：房间、工作台、维修座、七号都有碰撞；场景另加看不见的边界（<see cref="FirstPersonBlocker"/>）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonWalker : MonoBehaviour
    {
        [SerializeField] FirstOrderCameraRig rig;
        [SerializeField] Transform eye;
        [SerializeField] float standEye = 1.55f, crouchEye = 1.12f;
        [SerializeField] float walkSpeed = 1.4f, fastSpeed = 2.3f, crouchSpeed = 0.7f;
        [Tooltip("鼠标灵敏度（度 / 像素）")]
        [SerializeField] float lookSensitivity = 0.09f;
        [SerializeField] float fov = 68f;
        [SerializeField] float startPitch = 18f;
        [Tooltip("伸手距离：准星对着的东西离眼睛超过这个距离就点不到（观察另有各自的看清距离）")]
        [SerializeField] float reach = 1.3f;

        public Transform Eye => eye;
        public float Fov => fov;
        public float Reach => reach;
        public float Yaw => yaw;
        public float Pitch => pitch;
        public bool Crouching => crouch;
        /// <summary>玩家按 Tab / Esc 放开了鼠标（去点界面）。</summary>
        public bool CursorReleased { get; private set; }
        /// <summary>这些条件任一成立时：鼠标放开、不移动、不转头（例如展开的手册、夜末面板）。</summary>
        public readonly List<Func<bool>> CursorRequests = new List<Func<bool>>();
        /// <summary>鼠标锁在画面中央（准星模式）。</summary>
        public bool Aiming => rig != null && rig.Walking && !CursorReleased && !Requested;
        /// <summary>这一帧的左键是用来重新锁住鼠标的（不当作操作）。</summary>
        public bool ClickConsumedThisFrame => consumedFrame == Time.frameCount;
        /// <summary>程序验收 / 测试用：代替键盘的移动输入（x 右，y 前），为 null 时读键盘。</summary>
        public Vector2? ScriptedMove { get; set; }
        /// <summary>测试用：不改系统鼠标锁定状态（编辑器批处理下锁鼠标没有意义）。</summary>
        public bool LeaveSystemCursor { get; set; }

        CharacterController cc;
        float yaw, pitch, vy, eyeH;
        bool crouch;
        int consumedFrame = -1;

        bool Requested { get { foreach (var f in CursorRequests) if (f != null && f()) return true; return false; } }

        public void Configure(FirstOrderCameraRig r, Transform e) { rig = r; eye = e; }

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            cc.minMoveDistance = 0f;   // 默认 1 mm：高帧率下（自检里约 650 帧 / 秒）每帧移动量小于它，会走不动
            yaw = transform.eulerAngles.y;
            pitch = startPitch;
            eyeH = standEye;
            ApplyPose();
            if (eye != null) eye.localPosition = new Vector3(0f, eyeH, 0f);
        }

        /// <summary>把玩家放到某处（脚底位置）、面向 yaw，抬头 / 低头 pitch。</summary>
        public void PlaceAt(Vector3 feet, float yawDeg, float pitchDeg = 10f)
        {
            if (cc == null) cc = GetComponent<CharacterController>();
            bool was = cc.enabled; cc.enabled = false;
            transform.position = feet;
            cc.enabled = was;
            yaw = yawDeg; pitch = pitchDeg; vy = 0f;
            ApplyPose();
        }

        /// <summary>转头（测试和程序验收用；真人用鼠标）。</summary>
        public void Look(float yawDeg, float pitchDeg) { yaw = yawDeg; pitch = Mathf.Clamp(pitchDeg, -80f, 80f); ApplyPose(); }

        /// <summary>让眼睛看向某个点。</summary>
        public void LookAt(Vector3 point)
        {
            var d = point - eye.position;
            Look(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
        }

        void ApplyPose()
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (eye != null) { eye.localPosition = new Vector3(0f, eyeH, 0f); eye.localRotation = Quaternion.Euler(pitch, 0f, 0f); }
        }

        void Update()
        {
            if (rig == null || eye == null) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            bool typing = FirstOrderInput.TextInputFocused;
            bool requested = Requested;

            if (kb != null && !typing && rig.Walking && !requested)
            {
                if (kb.tabKey.wasPressedThisFrame) CursorReleased = !CursorReleased;
                if (kb.escapeKey.wasPressedThisFrame) CursorReleased = true;
            }
            // 放开鼠标时点一下画面（不在界面上）：重新锁住，这一下不算操作
            if (rig.Walking && CursorReleased && !requested && mouse != null && mouse.leftButton.wasPressedThisFrame && !FirstOrderInput.IsOverUI(mouse.position.ReadValue()))
            {
                CursorReleased = false;
                consumedFrame = Time.frameCount;
            }
            if (!LeaveSystemCursor)
            {
                bool lockIt = Aiming && Application.isFocused;
                var want = lockIt ? CursorLockMode.Locked : CursorLockMode.None;
                if (Cursor.lockState != want) Cursor.lockState = want;
                if (Cursor.visible == lockIt) Cursor.visible = !lockIt;
            }

            Vector2 move = Vector2.zero;
            if (ScriptedMove.HasValue) move = ScriptedMove.Value;
            else if (kb != null && !typing && !requested)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
            }

            if (!rig.Walking)
            {
                // 固定机位（近看）：按移动键回到行走
                if (move.sqrMagnitude > 0f && rig.FirstPersonEnabled && !requested) rig.Walk();
                return;
            }

            if (kb != null && !typing && !requested && kb.cKey.wasPressedThisFrame) crouch = !crouch;
            bool crouchNow = crouch || kb != null && !typing && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);

            if (Aiming && mouse != null && !LeaveSystemCursor)
            {
                var d = mouse.delta.ReadValue();
                yaw += d.x * lookSensitivity;
                pitch = Mathf.Clamp(pitch - d.y * lookSensitivity, -80f, 80f);
            }

            float speed = crouchNow ? crouchSpeed : kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) && !ScriptedMove.HasValue ? fastSpeed : walkSpeed;
            // Loading/render stalls must not turn the next input frame into a large physical step.
            float movementDelta = Mathf.Min(Time.deltaTime, .05f);
            if (move.sqrMagnitude > 1f) move.Normalize();
            var dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y);
            vy = cc.isGrounded ? -0.5f : vy - 9.81f * movementDelta;
            if (requested) dir = Vector3.zero;
            cc.Move((dir * speed + Vector3.up * vy) * movementDelta);

            eyeH = Mathf.MoveTowards(eyeH, crouchNow ? crouchEye : standEye, 2.5f * movementDelta);
            ApplyPose();
        }

        void OnDisable()
        {
            if (!LeaveSystemCursor) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
    }

}
