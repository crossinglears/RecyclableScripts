/*
Name: CharacterMovement RootMotion
Description: Click-to-move character controller using NavMeshAgent, Animator root motion, DOTween speed blending, interactable targeting, look-only behavior for nearby targets, and double-click run input.
Tags: movement, navmesh, rootmotion, inputsystem, interactable, dotween
Dependencies: DG.Tweening, Unity.InputSystem
*/

using DG.Tweening;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class CharacterMovement_RootMotion : MonoBehaviour
{
    [Header("References")]
    public NavMeshAgent navMeshAgent;
    public Animator animator;

    [SerializeField] private Camera cam;

    [Header("Layer Masks")]
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private LayerMask interactableMask;

    [Header("Movement Settings")]
    [SerializeField] private float sampleDistance = 2f;
    [SerializeField] private float stopDistance = 0.1f;
    [SerializeField] private float lookOnlyDistance = 1f;
    [SerializeField] private float lookRotationThreshold = 2f;
    [SerializeField] private float speedSwitchTime = 1f;
    [SerializeField] private float doubleClickThreshold = 0.25f;
    [SerializeField] private bool allowRunning = true;

    [Header("Runtime")]
    public Interactable lastInteractable;

    private bool destinationReachedEventFired = false;
    private Vector3 lookOnlyTarget;
    private bool lookOnlyActive;
    private float lastClickTime;

    private static CharacterMovement_RootMotion _instance;
    public static CharacterMovement_RootMotion Instance
    {
        get => _instance;
        set
        {
            if (value == null)
            {
                _instance = null;
            }
            else if (_instance == null)
            {
                _instance = value;
            }
            else if (_instance != value)
            {
                Destroy(value);
                Debug.Log($"There should only ever be one instance of {nameof(CharacterMovement_RootMotion)}!");
            }
        }
    }

    private void Awake()
    {
        Instance = this;

        if (cam == null)
            cam = Camera.main;
    }

    private void Start()
    {
        if (navMeshAgent == null || animator == null)
        {
            Debug.LogError($"{nameof(CharacterMovement_RootMotion)} is missing required references.", this);
            enabled = false;
            return;
        }

        navMeshAgent.updatePosition = false;
        navMeshAgent.updateRotation = false;
    }

    private void Update()
    {
        bool doubleClicked = false;
        bool runningMode = animator.GetFloat("Input Magnitude") > 0.5f;
        bool clickTrigger = Mouse.current.leftButton.wasPressedThisFrame ||
                            (runningMode && Mouse.current.leftButton.isPressed);

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            float time = Time.time;
            doubleClicked = time - lastClickTime <= doubleClickThreshold;
            lastClickTime = time;
        }

        if (clickTrigger)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f, interactableMask | groundMask))
            {
                Vector3 targetPos;
                lastInteractable = hit.collider.GetComponent<Interactable>();

                if (lastInteractable != null)
                {
                    if (lastInteractable.TargetDestination != null)
                    {
                        if (!NavMesh.SamplePosition(lastInteractable.TargetDestination.position, out NavMeshHit navHit, sampleDistance, NavMesh.AllAreas))
                            return;

                        targetPos = navHit.position;
                    }
                    else
                    {
                        targetPos = hit.point;
                    }
                }
                else
                {
                    if (!NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, sampleDistance, NavMesh.AllAreas))
                        return;

                    targetPos = navHit.position;
                }

                Vector3 toClick = targetPos - transform.position;

                if (toClick.magnitude <= lookOnlyDistance)
                {
                    navMeshAgent.ResetPath();
                    lookOnlyTarget = targetPos;
                    lookOnlyActive = true;
                    return;
                }

                lookOnlyActive = false;
                navMeshAgent.SetDestination(targetPos);
                destinationReachedEventFired = false;
            }

            if (allowRunning && doubleClicked)
            {
                animator.DOKill();
                DOTween.To(
                    () => animator.GetFloat("Input Magnitude"),
                    x => animator.SetFloat("Input Magnitude", x),
                    1f,
                    speedSwitchTime);
            }
        }

        if (allowRunning && !Mouse.current.leftButton.isPressed && animator.GetFloat("Input Magnitude") > 0f)
        {
            animator.DOKill();
            DOTween.To(
                () => animator.GetFloat("Input Magnitude"),
                x => animator.SetFloat("Input Magnitude", x),
                0f,
                speedSwitchTime);
        }

        if (lookOnlyActive)
        {
            Vector3 lookDir = lookOnlyTarget - transform.position;
            lookDir.y = 0f;

            if (lookDir.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    navMeshAgent.angularSpeed * Time.deltaTime);

                float angle = Quaternion.Angle(transform.rotation, targetRotation);
                if (angle <= lookRotationThreshold)
                {
                    lookOnlyActive = false;

                    if (lastInteractable != null)
                    {
                        lastInteractable.OnDestinationReached();
                        lastInteractable = null;
                    }
                }
            }

            animator.SetBool("IsMoving", false);
            return;
        }

        Vector3 toTarget = navMeshAgent.destination - transform.position;
        if (toTarget.magnitude <= stopDistance)
        {
            navMeshAgent.ResetPath();
            navMeshAgent.velocity = Vector3.zero;

            if (!destinationReachedEventFired)
            {
                destinationReachedEventFired = true;
                OnDestinationReached();
            }
        }

        Vector3 desiredVelocity = navMeshAgent.desiredVelocity;
        if (desiredVelocity.sqrMagnitude > 0.01f)
        {
            float distance = toTarget.magnitude;
            float speedMultiplier = Mathf.Clamp(distance, 1f, 15f);
            Quaternion targetRotation = Quaternion.LookRotation(desiredVelocity);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                navMeshAgent.angularSpeed * speedMultiplier * Time.deltaTime);
        }

        animator.SetBool("IsMoving", navMeshAgent.velocity.magnitude > 0.1f);
    }

    private void OnAnimatorMove()
    {
        Vector3 rootPosition = animator.rootPosition;

        if (NavMesh.SamplePosition(rootPosition, out NavMeshHit hit, sampleDistance, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            navMeshAgent.nextPosition = hit.position;
        }
    }

    public void OnDestinationReached()
    {
        animator.DOKill();

        if (allowRunning)
        {
            DOTween.To(
                () => animator.GetFloat("Input Magnitude"),
                x => animator.SetFloat("Input Magnitude", x),
                0f,
                speedSwitchTime);
        }

        Debug.Log("Destination Reached");

        if (lastInteractable != null)
        {
            lastInteractable.OnDestinationReached();
            lastInteractable = null;
        }
    }
}
