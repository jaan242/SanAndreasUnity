﻿using SanAndreasUnity.Behaviours.Vehicles;
using SanAndreasUnity.Behaviours.World;
using SanAndreasUnity.Importing.Animation;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using SanAndreasUnity.Utilities;
using System.Collections.Generic;
using System.Linq;
using SanAndreasUnity.Net;

namespace SanAndreasUnity.Behaviours
{
	[RequireComponent(typeof(CharacterController))]
    public partial class Ped : 
		#if MIRROR
		Mirror.NetworkBehaviour
		#else
		MonoBehaviour
		#endif
    {
        
		private static List<Ped> s_allPeds = new List<Ped> ();
		public static Ped[] AllPeds { get { return s_allPeds.ToArray (); } }
		public static IEnumerable<Ped> AllPedsEnumerable => s_allPeds;
		public static int NumPeds => s_allPeds.Count;

		private WeaponHolder m_weaponHolder;
		public WeaponHolder WeaponHolder { get { return m_weaponHolder; } }

		private StateMachine m_stateMachine = new StateMachine ();

        public Camera Camera { get { return this == Ped.Instance ? Camera.main : null; } }
        public PedModel PlayerModel { get; private set; }

		public bool shouldPlayAnims = true;

        public CharacterController characterController { get; private set; }

		public float CameraDistance { get { return PedManager.Instance.cameraDistanceFromPed; } set { PedManager.Instance.cameraDistanceFromPed = value; } }

		public float CameraDistanceVehicle { get { return VehicleManager.Instance.cameraDistanceFromVehicle; } set { VehicleManager.Instance.cameraDistanceFromVehicle = value; } }

		// used for clamping camera rotation
		[SerializeField] private Vector2 m_cameraClampValue = new Vector2(60, 60);
		public Vector2 CameraClampValue { get { return m_cameraClampValue; } set { m_cameraClampValue = value; } }

        
		public Peds.States.BaseScriptState[] States { get; private set; }
		public Peds.States.BaseScriptState CurrentState { get { return (Peds.States.BaseScriptState) m_stateMachine.CurrentState; } }

        public Cell Cell { get { return Cell.Instance; } }

		public UnityEngine.Animation AnimComponent { get { return PlayerModel.AnimComponent; } }

		public SanAndreasUnity.Importing.Items.Definitions.PedestrianDef PedDef { get { return this.PlayerModel.Definition; } }

		public static int RandomPedId {
			get {
				int count = Ped.SpawnablePedDefs.Count ();
				if (count < 1)
					throw new System.Exception ("No ped definitions found");

				int index = Random.Range (0, count);
				return Ped.SpawnablePedDefs.ElementAt (index).Id;
			}
		}

		public static int LayerMask { get { return UnityEngine.LayerMask.GetMask ("Player"); } }

        public bool IsGrounded { get { return characterController.isGrounded; } }

		public Vector2 MouseMoveInput { get; set; }
		public Vector2 MouseScrollInput { get; set; }

		public bool IsWalkOn { get; set; }
		public bool IsRunOn { get; set; }
		public bool IsSprintOn { get; set; }
		public bool IsJumpOn { get; set; }

        public Vector3 Velocity { get; private set; }
		/// <summary> Current movement input. </summary>
        public Vector3 Movement { get; set; }
		/// <summary> Direction towards which the player turns. </summary>
        public Vector3 Heading { get; set; }
        
		public bool IsAiming { get { return m_weaponHolder.IsAiming; } }
		public Weapon CurrentWeapon { get { return m_weaponHolder.CurrentWeapon; } }
		public bool IsFiring { get { return m_weaponHolder.IsFiring; } }
		public Vector3 AimDirection { get { return m_weaponHolder.AimDirection; } set => m_weaponHolder.AimDirection = value; }
		public Vector3 FirePosition => this.CurrentWeapon != null ? this.CurrentWeapon.GetFirePos() : this.transform.position;
		public Vector3 FireDirection => this.CurrentWeapon != null ? this.CurrentWeapon.GetFireDir() : this.AimDirection;
		public bool IsAimOn { get ; set ; }
		public bool IsFireOn { get ; set ; }
		public bool IsHoldingWeapon { get { return m_weaponHolder.IsHoldingWeapon; } }

		private Coroutine m_findGroundCoroutine;

		public struct FindGroundParams
		{
			public bool tryFromAbove;

			public static FindGroundParams Default => new FindGroundParams(){tryFromAbove = true};
			public static FindGroundParams DefaultBasedOnLoadedWorld => new FindGroundParams(){tryFromAbove = (null == Cell.Instance || Cell.Instance.HasExterior)};
		}


		/// <summary>Ped who is controlled by local player.</summary>
		public static Ped Instance { get { return Net.Player.Local != null ? Net.Player.Local.OwnedPed : null; } }

		/// <summary>Position of ped instance.</summary>
		public static Vector3 InstancePos { get { return Instance.transform.position; } }

		/// <summary>Is this ped controlled by local player ?</summary>
		public bool IsControlledByLocalPlayer { get { return this == Ped.Instance; } }

		public string DescriptionForLogging => "(netId = " + this.netId + ")";



        
        void Awake()
        {
            
			this.PlayerModel = this.GetComponentInChildren<PedModel>();
            this.characterController = this.GetComponent<CharacterController>();
			m_weaponHolder = GetComponent<WeaponHolder> ();

			this.States = this.GetComponentsInChildren<Peds.States.BaseScriptState> ();

			this.AwakeForDamage ();

			this.Awake_Net();

        }

        void Start()
        {
            //MySetupLocalPlayer ();

			this.Start_Net();

			this.StartForDamage ();

			if (NetStatus.IsServer)
			{
				if (null == this.CurrentState)
					this.SwitchState<Peds.States.StandState> ();
			}

			// register Cell focus point
			if (this.Cell != null)
			{
				if (NetStatus.IsServer)
				{
					// only register if this ped is owned by some player
					if (Player.GetOwningPlayer(this) != null)
						this.Cell.focusPoints.AddIfNotPresent(this.transform);
				}
				else if (NetStatus.IsClientActive())
				{
					// only register if this ped is owned by local player
					// TODO: IsControlledByLocalPlayer may not return true, because syncvar in Player script may
					// not be updated yet
					if (this.IsControlledByLocalPlayer)
						this.Cell.focusPoints.AddIfNotPresent(this.transform);
				}
			}

			if (!NetStatus.IsServer)
			{
				// destroy OutOfRangeDestroyer on clients - server should handle destroying peds which are not in range
				this.gameObject.DestroyComponent<OutOfRangeDestroyer>();
			}

			// find ground
			if (NetStatus.IsServer)
			{
				if (!this.IsGrounded)
				{
					this.FindGround (FindGroundParams.DefaultBasedOnLoadedWorld);
				}
			}

        }

		void OnEnable ()
		{
			s_allPeds.Add (this);
		}

		void OnDisable ()
		{
			s_allPeds.Remove (this);
		}


		public Peds.States.BaseScriptState GetState(System.Type type)
		{
			return this.States.FirstOrDefault (s => s.GetType ().Equals (type));
		}

		public T GetState<T>() where T : Peds.States.BaseScriptState
		{
			return (T) this.GetState(typeof(T));
		}

		public Peds.States.BaseScriptState GetStateOrLogError(System.Type type)
		{
			var state = this.GetState (type);
			if(null == state)
				Debug.LogErrorFormat ("Failed to find state: {0}", type);
			return state;
		}

		public T GetStateOrLogError<T>() where T : Peds.States.BaseScriptState
		{
			return (T) this.GetStateOrLogError(typeof(T));
		}

		public void SwitchState(System.Type type)
		{
			
			var state = this.GetStateOrLogError (type);
			if (null == state)
				return;
			
		//	IState oldState = this.CurrentState;

			m_stateMachine.SwitchState (state);

//			if (oldState != state)
//			{
//				Debug.LogFormat ("Switched to state: {0}", state.GetType ().Name);
//			}
			
		}

		public void SwitchState<T>() where T : Peds.States.BaseScriptState
		{
			this.SwitchState(typeof(T));
		}


		public void Teleport(Vector3 position, Quaternion rotation, FindGroundParams parameters) {

			if (!NetStatus.IsServer)
				return;

			if (this.IsInVehicle)
				return;

			this.transform.position = position;
			this.transform.rotation = rotation;
			this.Heading = rotation.TransformDirection(Vector3.forward);

			this.FindGround (parameters);

		}

		public void Teleport(Vector3 position, Quaternion rotation)
		{
			this.Teleport(position, rotation, FindGroundParams.DefaultBasedOnLoadedWorld);
		}

		public void Teleport(Vector3 position) {

			this.Teleport (position, this.transform.rotation);

		}

        public void FindGround (FindGroundParams parameters)
		{
			if (m_findGroundCoroutine != null) {
				StopCoroutine (m_findGroundCoroutine);
				m_findGroundCoroutine = null;
			}

			m_findGroundCoroutine = StartCoroutine (FindGroundCoroutine (parameters));
		}

        private IEnumerator FindGroundCoroutine(FindGroundParams parameters)
        {

			// set y pos to high value, so that higher grounds can be loaded
		//	this.transform.SetY (150);

			Vector3 startingPos = this.transform.position;

			yield return null;

			// wait for loader to finish, in case he didn't
			while (!Loader.HasLoaded)
				yield return null;

			// yield until you find ground beneath or above the player, or until timeout expires

			float timeStarted = Time.time;
			int numAttempts = 1;

			while (true) {
				
				if (Time.time - timeStarted > 4.0f) {
					// timeout expired
					Debug.LogWarningFormat("Failed to find ground for ped {0} - timeout expired", this.DescriptionForLogging);
					yield break;
				}

				// maintain starting position
				this.transform.position = startingPos;
				this.Velocity = Vector3.zero;

				RaycastHit hit;
				float raycastDistance = 1000f;
				// raycast against all layers, except player
				int raycastLayerMask = ~ Ped.LayerMask;

				var raycastPositions = new List<Vector3>{ this.transform.position };	//transform.position - Vector3.up * characterController.height;
				var raycastDirections = new List<Vector3>{ Vector3.down };
				var customMessages = new List<string>{ "from center" };

				if (parameters.tryFromAbove)
				{
					raycastPositions.Add (this.transform.position + Vector3.up * raycastDistance);
					raycastDirections.Add (Vector3.down);
					customMessages.Add ("from above");
				}

				for (int i = 0; i < raycastPositions.Count; i++) {

					if (Physics.Raycast (raycastPositions[i], raycastDirections[i], out hit, raycastDistance, raycastLayerMask)) {
						// ray hit the ground
						// we can move there

						this.OnFoundGround (hit, numAttempts, customMessages [i]);

						yield break;
					}

				}


				numAttempts++;
				yield return null;
			}

        }

		private void OnFoundGround(RaycastHit hit, int numAttempts, string customMessage) {

			this.transform.position = hit.point + Vector3.up * (characterController.height + 0.1f);
			this.Velocity = Vector3.zero;

			Debug.LogFormat ("Found ground at {0}, distance {1}, object name {2}, num attempts {3}, {4}, ped {5}", hit.point, hit.distance, 
				hit.transform.name, numAttempts, customMessage, this.DescriptionForLogging);

		}

        
        private void Update()
        {
            if (!Loader.HasLoaded)
                return;

			if (this.CurrentState != null)
			{
				this.CurrentState.UpdateState ();
			}

			if (NetStatus.IsServer)
            	this.ResetIfFallingBelowTheWorld();

		//	ConstrainPosition ();

		//	ConstrainRotation ();

		//	UpdateAnims ();

            //if (IsDrivingVehicle)
            //    UpdateWheelTurning();

			this.UpdateDamageStuff ();

			this.Update_Net();

			if (this.CurrentState != null)
				this.CurrentState.PostUpdateState();

        }

		void LateUpdate ()
		{

			if (this.CurrentState != null)
			{
				this.CurrentState.LateUpdateState ();
			}

		}

		public void ConstrainPosition() {

			// Constrain to stay inside map

			/*
			if (transform.position.x < -3000)
			{
				var t = transform.position;
				t.x = -3000;
				transform.position = t;
			}
			if (transform.position.x > 3000)
			{
				var t = transform.position;
				t.x = 3000;
				transform.position = t;
			}
			if (transform.position.z < -3000)
			{
				var t = transform.position;
				t.z = -3000;
				transform.position = t;
			}
			if (transform.position.z > 3000)
			{
				var t = transform.position;
				t.z = 3000;
				transform.position = t;
			}
			*/

		}

		public void ConstrainRotation ()
		{
			if (IsInVehicle)
				return;

			// ped can only rotate around Y axis

			Vector3 eulers = this.transform.eulerAngles;
			if (eulers.x != 0f || eulers.z != 0f) {
				eulers.x = 0f;
				eulers.z = 0f;
				this.transform.eulerAngles = eulers;
			}

		}

		void ResetIfFallingBelowTheWorld()
		{

			if (this.IsInVehicle)
				return;

            if (!this.IsGrounded && this.transform.position.y < -300 && this.Velocity.y < -20)
            {
				// set velocity to 0
                this.Velocity = Vector3.zero;

				// restore ped to higher position, and try to find ground
				Vector3 t = this.transform.position;
				t.y = 150;
				this.transform.position = t;
				this.FindGround(FindGroundParams.Default);
				
            }

		}

        private void FixedUpdate()
        {
            if (!Loader.HasLoaded)
                return;

			if (this.CurrentState != null)
			{
				this.CurrentState.FixedUpdateState ();
			}

			this.FixedUpdate_Net();

        }

		public void UpdateHeading()
		{
			
			if (this.IsAiming && this.CurrentWeapon != null && !this.CurrentWeapon.CanTurnInDirectionOtherThanAiming) {
				// ped heading can only be the same as ped direction
				this.Heading = this.WeaponHolder.AimDirection;
			}

			// player can look only along X and Z axis
			this.Heading = this.Heading.WithXAndZ ().normalized;

		}

		public void UpdateRotation()
		{

			// rotate player towards his heading
			Vector3 forward = Vector3.RotateTowards (this.transform.forward, Heading, PedManager.Instance.pedTurnSpeed * Time.deltaTime, 0.0f);
			this.transform.rotation = Quaternion.LookRotation(forward);

		}

		public void UpdateMovement()
		{
			

			// movement can only be done on X and Z axis
			this.Movement = this.Movement.WithXAndZ ();

			// change heading to match movement input
			//if (Movement.sqrMagnitude > float.Epsilon)
			//{
			//	Heading = Vector3.Scale(Movement, new Vector3(1f, 0f, 1f)).normalized;
			//}

			// change velocity based on movement input and current speed extracted from anim

			float modelVel = Mathf.Abs( PlayerModel.Velocity [PlayerModel.VelocityAxis] );
			//Vector3 localMovement = this.transform.InverseTransformDirection (this.Movement);
			//Vector3 globalMovement = this.transform.TransformDirection( Vector3.Scale( localMovement, modelVel ) );

			Vector3 vDiff = this.Movement * modelVel - new Vector3(Velocity.x, 0f, Velocity.z);
			Velocity += vDiff;

			// apply gravity
			Velocity = new Vector3(Velocity.x, characterController.isGrounded
				? 0f : Velocity.y - (-Physics.gravity.y) * 2f * Time.fixedDeltaTime, Velocity.z);


			// finally, move the character
			characterController.Move(Velocity * Time.fixedDeltaTime);
		

//			if(!IsLocalPlayer)
//            {
//                Velocity = characterController.velocity;
//            }

		}


		public void StartFiring ()
		{
			if (!this.IsAiming)
				return;

			((Peds.States.IAimState)this.CurrentState).StartFiring();
		}

		public void StopFiring ()
		{
			if (!this.IsFiring)
				return;

			((Peds.States.IFireState)this.CurrentState).StopFiring();
		}


		public void ResetInput ()
		{
			this.ResetMovementInput ();
			this.MouseMoveInput = Vector2.zero;
			this.MouseScrollInput = Vector2.zero;
			this.IsAimOn = this.IsFireOn = false;
		}

		public void ResetMovementInput ()
		{
			this.IsWalkOn = this.IsRunOn = this.IsSprintOn = false;
			this.Movement = Vector3.zero;
			this.IsJumpOn = false;
		}

		public void OnFireButtonPressed ()
		{
			if (this.CurrentState != null)
				this.CurrentState.OnFireButtonPressed ();
		}

		public void OnAimButtonPressed ()
		{
			if (this.CurrentState != null)
				this.CurrentState.OnAimButtonPressed ();
		}

		public void OnSubmitPressed ()
		{
			if (this.CurrentState != null)
			{
				this.CurrentState.OnSubmitPressed ();
			}
		}

		public void OnJumpButtonPressed ()
		{
			if (this.CurrentState != null)
				this.CurrentState.OnJumpPressed ();
		}

		public void OnCrouchButtonPressed ()
		{
			if (this.CurrentState != null)
				this.CurrentState.OnCrouchButtonPressed ();
		}

		public void OnNextWeaponButtonPressed ()
		{
			if (this.CurrentState != null)
				this.CurrentState.OnNextWeaponButtonPressed ();
		}

		public void OnPreviousWeaponButtonPressed ()
		{
			if (this.CurrentState != null)
				this.CurrentState.OnPreviousWeaponButtonPressed ();
		}

		public void OnFlyButtonPressed ()
		{
			if (this.CurrentState != null)
				this.CurrentState.OnFlyButtonPressed ();
		}

		public void OnFlyThroughButtonPressed ()
		{
			if (this.CurrentState != null)
				this.CurrentState.OnFlyThroughButtonPressed ();
		}


		void OnDrawGizmosSelected ()
		{

			// draw heading ray

			Gizmos.color = Color.blue;
			Gizmos.DrawLine (this.transform.position, this.transform.position + this.Heading);

			// draw movement ray

			Gizmos.color = Color.green;
			Gizmos.DrawLine (this.transform.position, this.transform.position + this.Movement);

		}

    }
}