using System.Collections.Generic;
using Gsn.Bingo.Analytics;
using Gsn.Bingo.Backend;
using Gsn.Bingo.BattlePass;
using Gsn.Bingo.Container;
using Gsn.Bingo.FishMeta;
using Gsn.Bingo.GameFeaturesInfo;
using Gsn.Bingo.Player;
using Gsn.Bingo.Rooms;
using Gsn.Bingo.Signals;
using Gsn.Bingo.Timer;
using Newtonsoft.Json;
using Zenject;

namespace Gsn.Bingo.AdminPopup
{
    public class LockedFeatureIconPopupServices : ILockedFeatureIconPopupServices
    {
        private const long DefaultDuration = 86400;

        private IContainerModel _containerModel;
        private IFishMetaService _fishMetaService;
        private IBattlePassService _battlePassService;
        private IGenericServerCallService _genericServerCallService;
        private IRoomModel _roomModel;
        private IUserPrefService _userPrefService;
        private ITimerService _timerService;
        private ISignalService _signalService;

        [Inject]
        public void Init(IContainerModel containerModel, IFishMetaService fishMetaService,
            IBattlePassService battlePassService, IGenericServerCallService genericServerCallService,
            IRoomModel roomModel, IUserPrefService userPrefService, ITimerService timerService,
            ISignalService signalService)
        {
            _containerModel = containerModel;
            _fishMetaService = fishMetaService;
            _battlePassService = battlePassService;
            _genericServerCallService = genericServerCallService;
            _roomModel = roomModel;
            _userPrefService = userPrefService;
            _timerService = timerService;
            _signalService = signalService;
        }

        public bool CanShowLockedFeatureIconPopup(AdminPopupData popupData)
        {
            var value = string.Empty;
            long duration = DefaultDuration;
            if (popupData.Texts?.TryGetValue("text35", out value) == true)
            {
                duration = long.Parse(value);
            }

            switch (popupData.FeatureType)
            {
                case FeatureType.FISH_META:
                {
                    return _fishMetaService.IsLockedFeatureAbTestEnabled()
                        && !_fishMetaService.IsFishMetaUnlocked
                        && IsValidTimeDuration(_userPrefService.GetFishMetaLockedIconClickedTimeStamp(), duration);
                }
                case FeatureType.SEASON_PASS:
                {
                    return !_battlePassService.IsBattlePassUnlocked()
                        && _containerModel.IsBattlePassLockIconFeatureEnabled
                        && IsValidTimeDuration(_userPrefService.GetBattlePassLockedIconClickedTimeStamp(), duration);
                }
                case FeatureType.FTUE_MINI_DAUBATHON:
                {
                    return IsValidTimeDuration(_userPrefService.GetDaubathonLockedIconClickedTimeStamp(), duration);
                }
                default:
                    return false;
            }
        }

        public bool ShowLockedFeatureIconPopup(AdminPopupData popupData)
        {
            if (popupData.FeatureType == FeatureType.FTUE_MINI_DAUBATHON)
            {
                LockedFeatureIconAnalytics(EventCodeConst.ICON, EventSubCodeConst.CLICK,
                    popupData.DefaultPopupAction.Value);

                return false;
            }

            switch (popupData.FeatureType)
            {
                case FeatureType.FISH_META:
                    _fishMetaService.ShowLockIconDialog();
                    LockedFeatureIconAnalytics(EventCodeConst.ICON, EventSubCodeConst.CLICK, "META");
                    break;
                case FeatureType.SEASON_PASS:
                    _battlePassService.ShowLockIconDialog();
                    break;
            }

            MakeGetDialogForIdCall(popupData.PopupId);
            return true;
        }

        public void OnOpenLockedFeatureIconPopup(AdminPopupData popupData)
        {
            LockedFeatureIconAnalytics(EventCodeConst.POPUP, EventSubCodeConst.SHOW, popupData.DefaultPopupAction.Value,
                popupData.DefaultPopupAction.Buttonlabel);

            if (popupData.DefaultPopupAction.Value == "DAUBATHON")
                _userPrefService.SetDaubathonLockedIconClickedTimeStamp(_timerService.CurrentGMTTime);
        }

        public void OnCloseLockedFeatureIconPopup(AdminPopupData popupData)
        {
            LockedFeatureIconAnalytics(EventCodeConst.POPUP, EventSubCodeConst.CLICK,
                popupData.DefaultPopupAction.Value,
                popupData.DefaultPopupAction.Buttonlabel);
        }

        private bool IsValidTimeDuration(long previousPopupOpenedTimeStamp, long duration)
        {
            return previousPopupOpenedTimeStamp + duration <= _timerService.CurrentGMTTime;
        }

        private void LockedFeatureIconAnalytics(string evtCodeConst, string evtSubCodeConst, string msgType,
            string uiElement = null)
        {
            var analyticData = new AnalyticEventFieldModel(EventGroup.LockedIcon,
                evtCodeConst, evtSubCodeConst);

            var otherData = new Dictionary<string, object>
            {
                { "state", "locked" }
            };

            analyticData.MessageType = msgType;

            if (uiElement != null)
                analyticData.UiElement = uiElement;

            analyticData.OtherDataJson = JsonConvert.SerializeObject(otherData);
            _signalService.Publish(new AnalyticSignal(analyticData));
        }

        private void MakeGetDialogForIdCall(string popupId)
        {
            _genericServerCallService.SendRequest<AdminPopupData>("Game.getDialogForId", popupId, null,
                _roomModel.RoomId);
        }
    }
}