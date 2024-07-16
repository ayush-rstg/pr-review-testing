using UnityEngine;

namespace Gsn.Bingo.AdminPopup
{
    public class BaseSaleAdminPopupView : BaseAdminPopupView
    {
        [SerializeField] private AdminPopupRewardsView adminPopupRewardsView;

        public AdminPopupRewardsView RewardsView => adminPopupRewardsView;

        public override void OnButtonCloseClick()
        {
            CloseDialog();
        }
    }
}