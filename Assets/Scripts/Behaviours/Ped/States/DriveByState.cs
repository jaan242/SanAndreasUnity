using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SanAndreasUnity.Behaviours.Peds.States
{
    public class DriveByState : VehicleSittingState
    {
        public override void UpdateState()
        {

            base.UpdateState();

            if (!this.IsActiveState)
                return;


            if (m_isServer)
            {
                if (this.SwitchToNonDriveByState())
                    return;
            }

        }

        protected virtual bool SwitchToNonDriveByState()
        {
            // check if we should exit aiming state
            if (!m_ped.IsHoldingWeapon || !m_ped.IsAimOn)
            {
                //	Debug.LogFormat ("Exiting aim state, IsHoldingWeapon {0}, IsAimOn {1}", m_ped.IsHoldingWeapon, m_ped.IsAimOn);
                m_ped.SwitchState<VehicleSittingState>();
                return true;
            }
            return false;
        }
    }
}
