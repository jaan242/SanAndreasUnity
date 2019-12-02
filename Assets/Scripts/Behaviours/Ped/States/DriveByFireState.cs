using SanAndreasUnity.Behaviours.Vehicles;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SanAndreasUnity.Behaviours.Peds.States
{
    public class DriveByFireState : VehicleSittingState, IFireState
    {
        public void StartDriveByFire(Vehicle vehicle, Vehicle.Seat seat)
        {
            this.CurrentVehicle = vehicle;
            this.CurrentVehicleSeatAlignment = seat.Alignment;

            m_ped.SwitchState<DriveByFireState>();
        }

        public override void UpdateState()
        {

            base.UpdateState();

            if (!this.IsActiveState)
                return;


            if (m_isServer)
            {
                if (this.SwitchToNonDriveByFireState())
                    return;
            }

        }

        protected virtual bool SwitchToNonDriveByFireState()
        {
            if (!m_ped.IsFireOn)
            {
                StopFiring();
                return true;
            }
            return false;
        }

        public virtual void StopFiring()
        {
            m_ped.GetStateOrLogError<DriveByState>().StopDriveByFire(this.CurrentVehicle, this.CurrentVehicleSeat);
        }

    }
}
