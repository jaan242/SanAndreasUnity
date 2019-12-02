using SanAndreasUnity.Behaviours.Vehicles;

namespace SanAndreasUnity.Behaviours.Peds.States
{
    public class DriveByState : VehicleSittingState, IAimState
    {
        public void StartDriveBy(Vehicle vehicle, Vehicle.Seat seat)
        {
            this.CurrentVehicle = vehicle;
            this.CurrentVehicleSeatAlignment = seat.Alignment;

            m_ped.SwitchState<DriveByState>();
        }

        public void StopDriveByFire(Vehicle vehicle, Vehicle.Seat seat)
        {
            this.CurrentVehicle = vehicle;
            this.CurrentVehicleSeatAlignment = seat.Alignment;

            m_ped.SwitchState<DriveByState>();
        }

        public override void UpdateState()
        {

            base.UpdateState();

            if (!this.IsActiveState)
                return;


            if (m_isServer)
            {
                if (this.SwitchToNonDriveByState())
                    return;
                if (this.SwitchToFiringState())
                    return;
            }

        }

        protected virtual bool SwitchToNonDriveByState()
        {
            // check if we should exit aiming state
            if (!m_ped.IsHoldingWeapon || !m_ped.IsAimOn)
            {
                //	Debug.LogFormat ("Exiting aim state, IsHoldingWeapon {0}, IsAimOn {1}", m_ped.IsHoldingWeapon, m_ped.IsAimOn);
                m_ped.GetStateOrLogError<VehicleSittingState>().StopDriveBy(this.CurrentVehicle, this.CurrentVehicleSeat);
                return true;
            }
            return false;
        }

        protected virtual bool SwitchToFiringState()
        {
            if (m_ped.IsFireOn)
            {
                StartFiring();
                return true;
            }
            return false;
        }

        public virtual void StartFiring()
        {
            m_ped.GetStateOrLogError<DriveByFireState>().StartDriveByFire(this.CurrentVehicle, this.CurrentVehicleSeat);
        }

    }
}
