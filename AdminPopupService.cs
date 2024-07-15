using System;
using System.Collections.Generic;
using System.Linq;
using Gsn.Bingo.Ads;
using Gsn.Bingo.Albums;
using Gsn.Bingo.Analytics;
using Gsn.Bingo.AppMigration;
using Gsn.Bingo.AppMonitor;
using Gsn.Bingo.AssetManagement;
using Gsn.Bingo.Backend;
using Gsn.Bingo.BashLeagues;
using Gsn.Bingo.BattlePass;
using Gsn.Bingo.BoosterSale;
using Gsn.Bingo.Container;
using Gsn.Bingo.DailyLuckyDraw;
using Gsn.Bingo.Dialogs;
using Gsn.Bingo.Dialogs.Signals;
using Gsn.Bingo.DiscountedCardCost;
using Gsn.Bingo.EndlessSale;
using Gsn.Bingo.Events;
using Gsn.Bingo.FishMeta;
using Gsn.Bingo.FlashSale;
using Gsn.Bingo.Game;
using Gsn.Bingo.GameFeaturesInfo;
using Gsn.Bingo.LightningBooster;
using Gsn.Bingo.LoadingScreen;
using Gsn.Bingo.Lobby;
using Gsn.Bingo.Logger;
using Gsn.Bingo.MysteryBoxSale;
using Gsn.Bingo.OfferWall;
using Gsn.Bingo.Orchestration;
using Gsn.Bingo.OutOfCurrency;
using Gsn.Bingo.PickAPrize;
using Gsn.Bingo.PiggyBank;
using Gsn.Bingo.Player;
using Gsn.Bingo.Rooms;
using Gsn.Bingo.SalesBanner;
using Gsn.Bingo.Signals;
using Gsn.Bingo.SpriteLocalRepository;
using Gsn.Bingo.Store;
using Gsn.Bingo.Store.Model;
using Gsn.Bingo.Subscription;
using Gsn.Bingo.Timer;
using Gsn.Bingo.TournamentV3;
using Gsn.Bingo.Utils;
using Newtonsoft.Json;
using UniRx;
using UnityEngine;
using Zenject;
using State = Gsn.Bingo.AppMonitor.State;

namespace Gsn.Bingo.AdminPopup
{
    public class AdminPopupService : IAdminPopupService, IInitializable, IResettable
    {
        private const string ADMIN_POPUP = "ADMIN_POPUP";
        private const string ADMIN_DIALOG_EMPTY_RESPONSE = "ADMIN_DIALOG_EMPTY_RESPONSE";

        private readonly List<AdminPopupJob> _activePopupJobs = new();

        private Subject<Unit> _dialogObservable;
        private IDisposable _dialogInstanceDisposable;
        private CompositeDisposable _serviceDisposable;
        private int _starterPackOffsetTimeInSeconds;

        private IAdminPopupModel _adminPopupModel;
        private IDialogService _dialogService;
        private IContainerModel _containerModel;
        private IRoomModel _roomModel;
        private IPlayerBalanceService _playerBalanceService;
        private IGenericServerCallService _genericServerCallService;
        private IOrchestrator _orchestrator;
        private ISignalService _signalService;
        private IExternalAssetManagementService _externalAssetManagementService;
        private ILoadingScreenService _loadingScreenService;
        private IEventsService _eventsService;
        private IFlashSaleModel _flashSaleModel;
        private IPlayerService _playerService;
        private IGameService _gameService;
        private IUserPrefService _userPrefService;
        private ITimerService _timerService;
        private ILockedFeatureIconPopupServices _lockedFeatureIconPopupServices;
        private ISpriteLocalRepositoryService _spriteLocalRepositoryService;
        private IAppMigrationPromoService _appMigrationPromoService;
        private ISalesBannerService _salesBannerService;
        private IDiscountedCardCostService _discountedCardCostService;

        [Inject]
        private void Init(ISignalService signalService, IAdminPopupModel adminPopupModel, IDialogService dialogService,
            IContainerModel containerModel, IPlayerBalanceService playerBalanceService, IOrchestrator orchestrator,
            IGenericServerCallService genericServerCallService, IRoomModel roomModel, IEventsService eventsService,
            IExternalAssetManagementService externalAssetManagementService, ILoadingScreenService loadingScreenService,
            IFlashSaleModel flashSaleModel, IPlayerService playerService, IGameService gameService,
            IUserPrefService userPrefService, ITimerService timerService,
            ILockedFeatureIconPopupServices lockedFeatureIconPopupServices,
            ISpriteLocalRepositoryService spriteLocalRepositoryService,
            IAppMigrationPromoService appMigrationPromoService, ISalesBannerService salesBannerService,
            IDiscountedCardCostService discountedCardCostService)
        {
            _signalService = signalService;
            _adminPopupModel = adminPopupModel;
            _dialogService = dialogService;
            _containerModel = containerModel;
            _roomModel = roomModel;
            _playerBalanceService = playerBalanceService;
            _genericServerCallService = genericServerCallService;
            _orchestrator = orchestrator;
            _externalAssetManagementService = externalAssetManagementService;
            _loadingScreenService = loadingScreenService;
            _eventsService = eventsService;
            _flashSaleModel = flashSaleModel;
            _playerService = playerService;
            _gameService = gameService;
            _userPrefService = userPrefService;
            _timerService = timerService;
            _lockedFeatureIconPopupServices = lockedFeatureIconPopupServices;
            _spriteLocalRepositoryService = spriteLocalRepositoryService;
            _appMigrationPromoService = appMigrationPromoService;
            _salesBannerService = salesBannerService;
            _discountedCardCostService = discountedCardCostService;
        }

        public void Initialize()
        {
            _signalService.Receive<PlayerDataReadySignal>().Subscribe(PlayerDataReady);
        }

        public void Reset()
        {
            _serviceDisposable?.Dispose();
            _serviceDisposable = null;
        }

        public short IsCardTokensEnable(string cityId)
        {
            var eventData = GetEventDataByCityId(cityId);
            return (short)(eventData?.CardTokens ?? 0);
        }

        public short GiftBoxTokenForEvent(string cityId)
        {
            var eventData = GetEventDataByCityId(cityId);
            return (short)(eventData?.GiftTokens ?? 0);
        }

        public AdminPopupData GetEventDataByCityId(string cityId)
        {
            return _adminPopupModel.GetEventEnabledPopup(_playerService.StartupFlags.EventIconShowBufferInSecs, cityId);
        }

        public void Show(AdminPopupData popupData, string origin = BingoConstants.ORIGIN_OTHERS)
        {
            var adminDialogData = new AdminDialogData
            {
                Origin = origin,
                PopupData = popupData,
            };

            ShowByTriggerPoint(AdminPopupTriggerPoints.None, adminDialogData);
        }

        public bool DoAction(PopupAction actionData, AdminDialogData dialogData)
        {
            var canWait = false;
            switch (actionData.Type)
            {
                case AdminPopupActionType.SO_CLOSE:
                    //TODO : Trigger InComplete Collection popup
                    break;
                case AdminPopupActionType.CONNECT_FACEBOOK:
                    PlayerPrefs.SetInt("FBAccountSwitch", 1);
                    _gameService.AppRestart(AppRestartReason.AccountSwitch);
                    break;
                case AdminPopupActionType.ROOM:
                    if (actionData.Value == "JOIN_LOBBY")
                    {
                        if (_orchestrator.IsInState(_orchestrator.GetState<RoomState>()))
                            _gameService.ExitRoom();
                        else if (_orchestrator.IsInState(_orchestrator.GetState<MinigameState>()))
                            _gameService.ExitMiniGame();
                    }
                    else
                    {
                        var roomIds = actionData.Value.Split(',').ToList();
                        foreach (var cityData in roomIds.Select(roomId => _containerModel.GetCityById(roomId))
                            .Where(cityData => cityData != null))
                        {
                            actionData.Value = cityData.Id;
                            break;
                        }

                        ServerDebugLog.RoomLoadInfo(
                            $"AdminPopupService : Trigger EnterRoomSignal , CityId={actionData.Value}");

                        _signalService.Publish(new EnterRoomSignal
                        {
                            CityId = actionData.Value,
                            States = new[] { State.Any }
                        });
                    }

                    break;
                case AdminPopupActionType.OPEN_DIALOG:
                    Show(actionData.Value, "");
                    break;
                case AdminPopupActionType.VERIFY_EMAIL:
                    _signalService.Publish(new ShowRegisterEmailPopupSignal(dialogData, actionData));
                    break;
                case AdminPopupActionType.OPEN_URL:
                    _playerService.AppInBackgroundOrigin = ADMIN_POPUP;
                    Application.OpenURL(actionData.Value);
                    break;
                case AdminPopupActionType.SHOP:
                    if (actionData.Value.IsNullOrEmpty())
                        actionData.Value = "1";

                    ShowShop(actionData.Value.ToInt(), dialogData.Origin);
                    break;
                case AdminPopupActionType.BUY_CHIPS:
                    ShowShop(1, dialogData.Origin);
                    break;
                case AdminPopupActionType.BUY_COINS:
                    ShowShop(2);
                    break;
                case AdminPopupActionType.BUY_ROCKETS:
                    ShowShop(4, dialogData.Origin);
                    break;
                case AdminPopupActionType.OPEN_EVENT:
                    _eventsService.ShowEvent(actionData.Value);
                    break;
                case AdminPopupActionType.PIGGYBANK:
                    _signalService.Publish<PiggyBankTriggerSignal>();
                    break;
                case AdminPopupActionType.DAILY_LUCKY_DRAW:
                    _signalService.Publish(new DailyLuckyDrawDialogTriggerSignal
                    {
                        Type = actionData.Value
                    });

                    break;
                case AdminPopupActionType.ACTION_TYPE_SUPERSONIC_VIDEO:
                    _signalService.Publish(
                        dialogData.PopupData.TriggerPoint.Exists(x =>
                            x.Value == AdminPopupTriggerType.SupersonicVideoSuccess)
                            ? new ShowVideoAdSignal { Origin = AdsUiOriginType.Additional }
                            : new ShowVideoAdSignal { Origin = AdsUiOriginType.Floating });

                    break;
                case AdminPopupActionType.ACTION_TYPE_OFFER_WALL_ADS:
                    _signalService.Publish(new ShowOfferWallSelectionDialogSignal());
                    break;
                case AdminPopupActionType.OPEN_AQUARIUM:
                    _signalService.Publish(new ShowAquariumSignal { Origin = "admin_popup" });
                    break;
                case AdminPopupActionType.OPEN_FISHPEDIA:
                    _signalService.Publish(new ShowFishPediaSignal
                    {
                        Origin = dialogData.PopupData.PopupType,
                        AquariumId = 1,
                        SelectTabCategoryId = int.TryParse(actionData.Value, out var value) ? value : 0
                    });

                    break;
                case AdminPopupActionType.OPEN_BATTLE_PASS:
                    _signalService.Publish(new ShowBattlePassSignal
                    {
                        Source = "other",
                        AutoScrollTaskId = "-1",
                        ShowPremiumPassPopup = actionData.Value == AdminPopupDataConstants.SHOW_PREMIUM_PASS_POPUP
                    });

                    break;
                case AdminPopupActionType.ULTIMATE_PASS_SPECIAL_CHALLENGE:
                {
                    var autoScrollTaskId = string.Empty;

                    if (_playerService.BattlePassUserData is { SpecialChallenges: { BattlePassTasks: { Count: > 0 } } })
                        autoScrollTaskId = _playerService.BattlePassUserData.SpecialChallenges.BattlePassTasks.First()
                            .Value.Id;

                    if (string.IsNullOrEmpty(autoScrollTaskId)) break;

                    _signalService.Publish(new ShowBattlePassSignal
                    {
                        Source = "other",
                        AutoScrollTaskId = autoScrollTaskId,
                        ShowPremiumPassPopup = actionData.Value == AdminPopupDataConstants.SHOW_PREMIUM_PASS_POPUP,
                        ShowSpecialChallenge = true
                    });
                }

                    break;
                case AdminPopupActionType.MINI_GAME:
                    //TODO : Need to add code to enter minigame from room and minigame state.
                    if (!actionData.Value.IsNullOrEmpty() &&
                        _orchestrator?.IsInState(_orchestrator.GetState<LobbyState>()) == true)
                    {
                        _signalService.Publish(new SelectRoomSignal
                        {
                            Id = actionData.Value,
                            IsRoom = false
                        });
                    }

                    break;
                case AdminPopupActionType.OPEN_BASH_TOURNEY:
                    _signalService.Publish(new ShowTournamentV3MainScreenSignal());
                    break;
                case AdminPopupActionType.BASH_TOURNEY_TEASER:
                    _signalService.Publish(new TriggerTournamentV3TeaserDialogSignal());
                    break;
                case AdminPopupActionType.APP_MIGRATION_PROMO:
                    _appMigrationPromoService.ShowAppMigrationPromoPopup();
                    break;
                case AdminPopupActionType.ENDLESS_SALE:
                    _signalService.Publish(new EndlessSaleDialogTriggerSignal
                    {
                        PopupId = dialogData.PopupData.PopupId,
                        Origin = dialogData.Origin,
                        SaleTitle = dialogData.PopupData.Title
                    });

                    break;
                case AdminPopupActionType.MYSTERY_BOX_SALE:
                    var address = _containerModel.MysteryBoxSaleConfigData?.AssetBundleUrl?.MainPopupUrl;
                    if (string.IsNullOrEmpty(address)) return canWait;

                    var data = new BaseDialogData
                    {
                        Origin = dialogData.Origin
                    };

                    _dialogService.LoadAndShowDialog<MysteryBoxSaleDialogView, BaseDialogData>(data, true, address);
                    break;
                case AdminPopupActionType.OPEN_BASH_LEAGUES:
                    _signalService.Publish(new ShowBashLeaguesLeaderBoardSignal { Origin = "admin_popup" });
                    break;
                case AdminPopupActionType.OPEN_ALBUMS:
                    _signalService.Publish(new ShowAlbumsSignal { Origin = "admin_popup" });
                    break;
                case AdminPopupActionType.BOOSTER_SALE:
                    canWait = true;
                    _signalService.PublishAsync(new BoosterSaleDialogTriggerSignal
                    {
                        Origin = dialogData.PopupData.TriggerPoint[0].Value,
                        AdminPopupData = dialogData.PopupData
                    }).DoOnError(_ => TriggerNextPopup()).Subscribe();

                    break;
                case AdminPopupActionType.ACTION_TYPE_PICK_A_PRIZE:
                    if (_playerService.StartupFlags.PickAPrizeData is { Enabled: true })
                    {
                        _signalService.Publish(new ShowPickAPrizeMainScreenSignal
                        {
                            OriginType = PickAPrizeTriggerOrigin.IntroPopup
                        });
                    }

                    break;
                default:
                    Log.Info(LogChannels.AdminPopup,
                        $"<color=#ff00ff>Admin Popup :: Unknown Action Type : {actionData.Type} </color>");

                    break;
            }

            return canWait;
        }

        private void PlayerDataReady(PlayerDataReadySignal _)
        {
            Reset();
            _serviceDisposable = new CompositeDisposable();
            _signalService.Subscribe<AdminPopupTriggerSignal>(OnNewPopupTriggerSignal).AddTo(_serviceDisposable);
            _signalService.Receive<ShowAdminPopupByIdSignal>().Subscribe(OnShowAdminPopupByIdSignal)
                .AddTo(_serviceDisposable);

            _signalService.Receive<DialogCloseSignal>().Subscribe(OnCloseDialogListener).AddTo(_serviceDisposable);
        }

        private IObservable<Unit> OnNewPopupTriggerSignal(AdminPopupTriggerSignal signal)
        {
            var triggerPoints = signal.Value;
            if (triggerPoints.All(x => x.Value != AdminPopupTriggerPoints.BuyComboConfirm.Value))
            {
                _activePopupJobs.Clear();
            }

            foreach (var triggerPoint in triggerPoints)
            {
                if (!IsEligibleToShowByState(triggerPoint)) continue;

                var popupList = _adminPopupModel.GetPopupListByTriggerPoint(triggerPoint);

                if (popupList == null) continue;

                for (var i = 0; i < popupList.Count; i++)
                {
                    var popupData = popupList[i];
                    if (CanShow(triggerPoint, popupData, out var isDataDeleted))
                    {
                        popupData.Frequency--;
                        if (CanRemovePopupData(triggerPoint, popupData) && ClearData(triggerPoint, popupData))
                            i--;

                        if (triggerPoint.Value == AdminPopupTriggerPoints.BuyComboConfirm.Value)
                        {
                            _activePopupJobs.Insert(0, new AdminPopupJob(triggerPoint, popupData));
                        }
                        else
                        {
                            _activePopupJobs.Add(new AdminPopupJob(triggerPoint, popupData));
                        }
                    }

                    if (isDataDeleted)
                        i--;
                }
            }

            if (_activePopupJobs.Count > 0)
                _activePopupJobs.Sort((x, y) => x.PopupData.Priority.CompareTo(y.PopupData.Priority));
            else
                return Observable.ReturnUnit();

            if (_activePopupJobs.Count > 1 &&
                triggerPoints.Exists(x => x.Value == AdminPopupTriggerPoints.OocLocAdminSale.Value))
            {
                _activePopupJobs.Sort((x, y) =>
                    x.PopupData.Rules.OocLocPriority.CompareTo(y.PopupData.Rules.OocLocPriority));

                _activePopupJobs.RemoveRange(1, _activePopupJobs.Count - 1);
            }

            _dialogObservable = new Subject<Unit>();

            if (DialogService.TotalOnScreenDialogs > 0)
                return _dialogObservable.AsObservable();

            var isPopupOpened = ShowNextPopup();
            return !isPopupOpened ? Observable.ReturnUnit() : _dialogObservable.AsObservable();
        }

        private void OnShowAdminPopupByIdSignal(ShowAdminPopupByIdSignal signal)
        {
            Show(signal.PopupId, signal.Origin);
        }

        private void OnCloseDialogListener(DialogCloseSignal signal)
        {
            if (DialogService.TotalOnScreenDialogs > 0) return;

            TriggerNextPopup();
        }

        private bool CanShow(TypeName triggerPoint, AdminPopupData popupData, out bool isDataDeleted)
        {
            var canShow = true;
            isDataDeleted = false;

            if (IsTimerExpired(triggerPoint, popupData))
            {
                isDataDeleted = true;
                return false;
            }

            if (popupData.Rules != null)
            {
                if (popupData.Rules.DisplayLevel != 0 &&
                    popupData.Rules.DisplayLevel > _playerBalanceService.Level)
                    return false;

                if (popupData.Rules.DisplayLevelMax != 0 &&
                    popupData.Rules.DisplayLevelMax <= _playerBalanceService.Level)
                    return false;

                if (triggerPoint == AdminPopupTriggerPoints.OocLocAdminSale)
                    return true;
            }

            if (popupData.DefaultPopupAction.Type == AdminPopupActionType.LOCKED_FEATURE_ICON)
            {
                return _lockedFeatureIconPopupServices.CanShowLockedFeatureIconPopup(popupData);
            }

            if (popupData.IsDaubathonMiniEvent && popupData.EventEndSeconds < 0)
                return false;

            if (popupData.IsRoomLeverPopup && (!popupData.CanShowRoomLeverPopup || _activePopupJobs.Find(job =>
                job.PopupData?.IsRoomLeverPopup == true || job.PopupData?.IsTriggerBasedSale == true) != null))
                return false;

            if (popupData.IsSubscriptionSale && _playerService.SubscriptionInfo != null &&
                _playerService.SubscriptionInfo.Status == 1) return false;

            if (popupData.IsUnlimitedFreePlaySale && _playerService.UnlimitedFreePlaySecondsRemaining > 0) return false;

            if (popupData.IsFlashSale)
            {
                if (!_flashSaleModel.CanShowPopup) return false;

                if (popupData.EndTime > 0 && popupData.EndTime <= _timerService.CurrentGMTTime) return false;
            }

            if (!_adminPopupModel.CanShowFtueSale(popupData))
                return false;

            if ((popupData.IsFtueDailySale || popupData.IsTriggerBasedBannerSale) && !popupData.CanShowTriggerBasedSale)
                return false;

            if (popupData.IsTriggerBasedSale && (IsRoomLeverActive() ||
                _activePopupJobs.Find(job => job.PopupData?.IsRoomLeverPopup == true) != null))
                return false;

            if (!CanShowLosingStreakSale(popupData, triggerPoint))
                return false;

            if (IsGameEndRoundEnabled(popupData) && !CanShowGameEndSale(popupData, triggerPoint))
                return false;

            if (popupData.PopupType == AdminPopupType.APP_RATING)
            {
                // ReSharper disable once RedundantAssignment
                // ReSharper disable once InlineOutVariableDeclaration
                var value = string.Empty;
                if (popupData.Texts?.TryGetValue("text1", out value) == true &&
                    int.TryParse(value, out var roundCount) && roundCount > _userPrefService.GetBingoRoundCount())
                    return false;
            }

            if (popupData.Extra != null)
            {
                if (popupData.Extra.ChipsCheckType.IsNotNullOrEmpty())
                    canShow = ValidateBalance(_playerBalanceService.Chips, popupData.Extra.Chips,
                        popupData.Extra.ChipsCheckType);

                if (popupData.Extra.CoinsCheckType.IsNotNullOrEmpty())
                    canShow = ValidateBalance(_playerBalanceService.Coins, popupData.Extra.Coins,
                        popupData.Extra.CoinsCheckType);

                if (CanCheckPowerPlayBalance(popupData, triggerPoint))
                {
                    if (popupData.Extra.PowerUpsCheckType.IsNotNullOrEmpty())
                        canShow = ValidateBalance(_playerBalanceService.PowerPlaysTotal, popupData.Extra.PowerUps,
                            popupData.Extra.PowerUpsCheckType);
                }

                if (popupData.Extra.RocketsCheckType.IsNotNullOrEmpty())
                    canShow = ValidateBalance(_playerBalanceService.Rockets, popupData.Extra.Rockets,
                        popupData.Extra.RocketsCheckType);
            }

            if (_orchestrator.IsInState(_orchestrator.GetState<RoomState>()))
            {
                if (popupData.EnabledRooms != null &&
                    !popupData.EnabledRooms.Contains(AdminPopupModel.DISPLAY_IN_ALL_ROOMS) &&
                    !popupData.EnabledRooms.Contains(_roomModel.RoomId) ||
                    popupData.ExcludedRooms != null && popupData.ExcludedRooms.Contains(_roomModel.RoomId))

                {
                    canShow = false;
                }
            }

            if (popupData.Texts == null || !popupData.Texts.ContainsKey("text40")) return canShow;

            var analyticPopupData = new AnalyticEventFieldModel(AnalyticsSubscriptionConstants.BStash,
                EventCodeConst.BUTTON, EventSubCodeConst.CLICK)
            {
                ScreenName = AnalyticsSubscriptionConstants.BenefitsScreenName,
                ScreenId = AnalyticsPopupIdConst.BASH_STASH_BENEFITS_POPUP,
                UiElement = "claim",
            };

            if (_playerService.SubscriptionInfo != null)
            {
                var otherData = new Dictionary<string, object>
                {
                    { "bonus_day", _playerService.SubscriptionInfo.CurrentDay }
                };

                analyticPopupData.OtherDataJson = JsonConvert.SerializeObject(otherData);
            }

            _signalService.Publish(new AnalyticSignal(analyticPopupData));

            return canShow;
        }

        private bool IsEligibleToShowByState(TypeName triggerPoint)
        {
            return triggerPoint.Value != AdminPopupTriggerPoints.StartUp.Value ||
                _orchestrator.IsInState(_orchestrator.GetState<LobbyState>());
        }

        private bool ShowNextPopup()
        {
            while (true)
            {
                if (_activePopupJobs == null || _activePopupJobs.Count == 0)
                    return false;

                var job = _activePopupJobs.FirstOrDefault();
                _activePopupJobs.RemoveAt(0);

                if (job == null) continue;

                if (!IsEligibleToShowByState(job.TriggerPoint) || job.PopupData == null)
                {
                    continue;
                }

                var adminDialogData = new AdminDialogData
                    { Origin = GetOrigin(job.TriggerPoint.Value), PopupData = job.PopupData };

                return ShowByTriggerPoint(job.TriggerPoint, adminDialogData);
            }
        }

        private void Show(string popupId, string origin)
        {
            _loadingScreenService.Open();
            _genericServerCallService.SendRequest<AdminPopupData>("Game.getDialogForId", dialogResponse =>
            {
                _loadingScreenService.Close();

                if (dialogResponse == null)
                    return;

                dialogResponse.StartTime = _timerService.CurrentGMTTime;
                dialogResponse.EndTime = dialogResponse.StartTime + dialogResponse.SecondsRemaining;

                _signalService.Publish(new AdminPopupGetDialogForIdDataReceivedSignal { Value = dialogResponse });

                var triggerPoint = dialogResponse.IsProgressiveSale || dialogResponse.IsDaubathonMiniEvent
                    ? AdminPopupTriggerPoints.StartUp
                    : dialogResponse.TriggerPoint[0];

                if (string.IsNullOrEmpty(origin))
                {
                    origin = GetOrigin(triggerPoint.Value);
                }

                var adminDialogData = new AdminDialogData
                {
                    Origin = origin,
                    PopupData = dialogResponse
                };

                ShowByTriggerPoint(triggerPoint, adminDialogData);
            }, popupId, null, _roomModel?.RoomId);
        }

        private void TriggerNextPopup()
        {
            _dialogInstanceDisposable?.Dispose();
            _dialogInstanceDisposable = null;

            if (_gameService.InTransition)
                return;

            var isNextDialogTriggered = ShowNextPopup();
            if (isNextDialogTriggered || _dialogObservable == null) return;

            _dialogObservable.OnNext(Unit.Default);
            _dialogObservable.OnCompleted();
            _dialogObservable = null;
        }

        private bool CanShowGameEndSale(AdminPopupData data, TypeName triggerPoint)
        {
            if (data == null)
                return false;

            if (triggerPoint.Value == AdminPopupTriggerPoints.BuyChipsCancel.Value)
                return true;

            if (data.DefaultPopupAction?.SubActionData?.GameEndRounds == null) return false;

            foreach (var round in data.DefaultPopupAction.SubActionData.GameEndRounds)
            {
                if (round == _playerService.BingoRoundCount.ToString())
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsGameEndRoundEnabled(AdminPopupData data)
        {
            return data?.DefaultPopupAction?.SubActionData?.GameEndRounds?.Length > 0;
        }

        private bool CanShowLosingStreakSale(AdminPopupData data, TypeName triggerPoint)
        {
            if (!IsLosingStreakEnabled(data) ||
                triggerPoint.Value == AdminPopupTriggerPoints.BuyPowerUpCancel.Value ||
                triggerPoint.Value == AdminPopupTriggerPoints.GameEnd.Value) return true;

            var canShow = true;
            var popupDictionary = _userPrefService.GetWithoutBingosRoundData();
            if (!popupDictionary.ContainsKey(data.PopupId) ||
                data.DefaultPopupAction.SubActionData.LosingStreakRound > popupDictionary[data.PopupId])
                canShow = false;

            return canShow;
        }

        private bool IsRoomLeverActive()
        {
            var roomLeversData = _adminPopupModel.GetRoomLeverPopups();
            return roomLeversData?.Find(popup =>
                popup.CanShowRoomLeverIcon && popup.IsActive(_timerService.CurrentGMTTime, 0)) != null;
        }

        private bool CanCheckPowerPlayBalance(AdminPopupData data, TypeName triggerPoint)
        {
            return !IsLosingStreakEnabled(data) || triggerPoint.Value != AdminPopupTriggerPoints.GameEndLost.Value;
        }

        private bool IsTimerExpired(TypeName triggerPoint, AdminPopupData popupData)
        {
            if (popupData.EventEndSeconds < 0 || popupData.IsActive(_timerService.CurrentGMTTime, 0))
                return false;

            popupData.Frequency = 0;
            ClearData(triggerPoint, popupData);

            return true;
        }

        private bool ValidateBalance(long userBalance, long checkValue, string checkType)
        {
            var canDisplay = false;
            var type = checkType.ToUpper();
            switch (type)
            {
                case "LS":
                    if (userBalance < checkValue)
                        canDisplay = true;

                    break;
                case "GR":
                    if (userBalance > checkValue)
                        canDisplay = true;

                    break;
                case "EQ":
                    if (userBalance == checkValue)
                        canDisplay = true;

                    break;
            }

            return canDisplay;
        }

        private bool ShowByTriggerPoint(TypeName triggerPoint, AdminDialogData adminDialogData)
        {
            TriggerAbTestAnalytic(adminDialogData);

            if (adminDialogData.PopupData.PopupEnabled || adminDialogData.PopupData.DefaultPopupAction.Type ==
                AdminPopupActionType.INAPP_COMBO_SALE)
            {
                Log.Info(LogChannels.AdminPopup,
                    $"<color=#ff00ff>Show Admin Popup Enabled = 1 for Trigger Point : {triggerPoint.Value} , Popup ID : {adminDialogData.PopupData.PopupId} , Title : {adminDialogData.PopupData.Title} </color>");

                switch (adminDialogData.PopupData.DefaultPopupAction.Type)
                {
                    case AdminPopupActionType.INTERACTIVE_INTRO_POPUP:
                        // TODO : Show How to play popup
                        return false;
                    case AdminPopupActionType.LOCKED_FEATURE_ICON:
                    {
                        return _lockedFeatureIconPopupServices.ShowLockedFeatureIconPopup(adminDialogData.PopupData) ||
                            StartShowPopupProcess(adminDialogData);
                    }
                    case AdminPopupActionType.LEVER_LIGHTNING_BOOSTER:
                    {
                        _signalService.Publish(new ShowLightningBoosterPopupSignal());
                        break;
                    }
                    case AdminPopupActionType.ACTION_TYPE_PICK_A_PRIZE_LEVER:
                    {
                        if (_playerService.StartupFlags.PickAPrizeData is { Enabled: true })
                        {
                            _signalService.Publish(new ShowPickAPrizeMainScreenSignal
                            {
                                OriginType = PickAPrizeTriggerOrigin.LeverIcon
                            });
                        }

                        break;
                    }
                    default:
                    {
                        if (triggerPoint == AdminPopupTriggerPoints.OutOfChips)
                        {
                            _signalService.Publish(
                                new ShowOutOfCurrencyPopupSignal(AnalyticPopupEventFieldModel.TagPopupOOC));

                            return DialogService.TotalOnScreenDialogs > 0;
                        }

                        if (triggerPoint != AdminPopupTriggerPoints.OutOfCoins)
                            return StartShowPopupProcess(adminDialogData);

                        _signalService.Publish(
                            new ShowOutOfCurrencyPopupSignal(AnalyticPopupEventFieldModel.TagPopupOOC,
                                CurrencyType.Coins));

                        return DialogService.TotalOnScreenDialogs > 0;
                    }
                }
            }

            Log.Info(LogChannels.AdminPopup,
                $"<color=#ff00ff>Show Admin Popup Enabled = 0 for Trigger Point : {triggerPoint.Value} , Popup ID : {adminDialogData.PopupData.PopupId} , Title : {adminDialogData.PopupData.Title} </color>");

            if (TriggerAction(adminDialogData.PopupData.DefaultPopupAction, adminDialogData)) return true;

            return DialogService.TotalOnScreenDialogs > 0 || ShowNextPopup();
        }

        private bool IsLosingStreakEnabled(AdminPopupData data)
        {
            return data?.DefaultPopupAction?.SubActionData?.LosingStreakRound > 0;
        }

        private bool StartShowPopupProcess(AdminDialogData dialogData)
        {
            if (dialogData == null)
                return false;

            var popupData = dialogData.PopupData;

            Log.Info(LogChannels.AdminPopup, $"StartShowPopupProcess : {popupData?.Title}");

            if (_externalAssetManagementService.HasCatalog == false)
            {
                Log.Warning(LogChannels.AdminPopup,
                    $"Skipping Admin popup for {popupData?.AssetAddress}, No catalog found!");

                return false;
            }

            var assetAddress = popupData?.AssetAddress;
            if (assetAddress.IsNullOrEmpty())
            {
                Log.Error(LogChannels.AdminPopup, "ADMIN_POPUP_SERVICE",
                    $"LiveOps invalid Address : {assetAddress}, Skipping...");

                return false;
            }

            var canShowPopup = true;
            var dialogDataObservable = Observable.ReturnUnit().ContinueWith(_ =>
            {
                return popupData == null || !popupData.Reload
                    ? Observable.ReturnUnit()
                    : _genericServerCallService
                        .SendRequest<AdminPopupData>("Game.getDialogForId", popupData.PopupId, null,
                            _roomModel.RoomId)
                        .Do(dialogResponse =>
                        {
                            if (dialogResponse == null)
                            {
                                canShowPopup = false;
                                Log.Info(LogChannels.AdminPopup, ADMIN_DIALOG_EMPTY_RESPONSE,
                                    $"Game.getDialogForId is null for PopupID : {popupData.PopupId}");

                                if (popupData.MaxAllowedPurchaseCount >= 0)
                                {
                                    RemoveAdminPopupReachedMaxAllowedPurchase(popupData);
                                }

                                return;
                            }

                            if (popupData.MaxAllowedPurchaseCount == 0)
                            {
                                canShowPopup = false;
                                RemoveAdminPopupReachedMaxAllowedPurchase(popupData);
                                return;
                            }

                            Log.Info(LogChannels.AdminPopup, "ADMIN_POPUP_SERVICE",
                                $"Game.getDialogForId Response : Popup ID : {dialogResponse.PopupId}");

                            dialogResponse.StartTime = _timerService.CurrentGMTTime;
                            dialogResponse.EndTime = dialogResponse.StartTime + dialogResponse.SecondsRemaining;
                            dialogResponse.IsLastInQueue = dialogData.PopupData.IsLastInQueue;

                            if (dialogResponse.IsTriggerBasedSaleLowOnChipsSale)
                            {
                                dialogResponse.Texts = dialogData.PopupData.Texts;
                                dialogResponse.DefaultPopupAction.Value =
                                    dialogData.PopupData.DefaultPopupAction.Value;
                            }

                            dialogData.PopupData = dialogResponse;

                            popupData.SecondsRemaining = dialogResponse.SecondsRemaining;
                            popupData.StartTime = dialogResponse.StartTime;
                            popupData.EndTime = dialogResponse.EndTime;
                            popupData.EventEndSeconds = dialogResponse.EventEndSeconds;
                            if (dialogResponse.AssetAddress.IsNotNullOrEmpty())
                            {
                                popupData.AssetAddress = dialogResponse.AssetAddress;
                            }

                            if (dialogResponse.DefaultPopupAction?.SubActionData?.IsDiscountedCardCostUnlocked > 0)
                            {
                                popupData.DefaultPopupAction.SubActionData.IsDiscountedCardCostUnlocked =
                                    dialogResponse.DefaultPopupAction.SubActionData.IsDiscountedCardCostUnlocked;

                                _discountedCardCostService.UpdateActiveTime(dialogResponse.DefaultPopupAction
                                    .SubActionData.DiscountedCardCostEndTime);

                                popupData.VerticaAdditionalTracking = dialogResponse.VerticaAdditionalTracking;
                            }
                        }).AsUnitObservable();
            }).ContinueWith(_ =>
            {
                return _externalAssetManagementService.LoadAsset<GameObject>(popupData?.AssetAddress)
                    .Do(_ =>
                    {
                        Log.Info(LogChannels.AdminPopup, "ADMIN_POPUP_SERVICE",
                            $"Asset {popupData?.AssetAddress} Download Completed");
                    });
            }).ContinueWith(handle =>
            {
                var imageUrls = popupData?.ThemesImages;
                if ((!imageUrls?.Any() ?? true) || handle?.Asset == null) return Observable.Return(handle);

                var spriteOperations = imageUrls.Select(url => _spriteLocalRepositoryService
                        .LoadTexture(url.Value, true, DateTime.Now.AddDays(7), true)
                        .Catch<Texture2D, Exception>(_ => Observable.Empty<Texture2D>()))
                    .ToList();

                return spriteOperations.WhenAll().Select(_ => handle);
            }).Select(handle =>
                new DialogServiceModel<AdminDialogData, IAssetHandle<GameObject>>(dialogData, handle,
                    canShowPopup ? 1 : 0));

            var dialogInstanceObservable =
                _dialogService.ShowDialog<BaseAdminPopupView, AdminDialogData>(dialogDataObservable, true);

            _dialogInstanceDisposable = dialogInstanceObservable.Subscribe(
                dialogInstance =>
                {
                    if (dialogInstance == null) return;

                    if (canShowPopup)
                    {
                        _signalService.Publish(new AdminPopupOpenedSignal { Value = popupData });

                        if (popupData?.Rules?.DisplayLevelMax > _playerBalanceService.Level)
                            _lockedFeatureIconPopupServices.OnOpenLockedFeatureIconPopup(popupData);
                    }

                    UnityHelper.FixEditorMaterialIssue(dialogInstance.AssetHandle.Asset);
                    dialogInstance.OnDialogClosed += () =>
                    {
                        dialogInstance.AssetHandle.Dispose();

                        _signalService.Publish(new AdminPopupClosedSignal { Value = popupData });

                        if (popupData?.Rules?.DisplayLevelMax > _playerBalanceService.Level)
                            _lockedFeatureIconPopupServices.OnCloseLockedFeatureIconPopup(popupData);
                    };
                },
                exception => { Log.Error(LogChannels.AdminPopup, "ADMIN_POPUP_SERVICE", exception.ToString()); });

            return true;
        }

        private bool ClearData(TypeName triggerPoint, AdminPopupData popupData)
        {
            if (triggerPoint == AdminPopupTriggerPoints.StartUp)
                popupData.Frequency = 0;

            if (popupData.Frequency >= 1)
                return false;

            _adminPopupModel.RemovePopupData(triggerPoint, popupData);

            return true;
        }

        private bool CanRemovePopupData(TypeName triggerPoint, AdminPopupData popupData)
        {
            return !popupData.TriggerPoint.Exists(x => x.Value == AdminPopupTriggerPoints.OocLocAdminSale.Value);
        }

        private void TriggerAbTestAnalytic(AdminDialogData adminDialogData)
        {
            var popupData = adminDialogData.PopupData;
            if (popupData == null || popupData.VerticaEventType.IsNullOrEmpty()) return;

            var data = AnalyticPopupEventFieldModel.CreateGeneralPopupEventData(popupData.PopupId, popupData.Title,
                EventGroup.AbTest, EventCodeConst.ABTEST, EventSubCodeConst.ABTEST, popupData.VerticaEventType,
                popupData.GetAbTestName(), popupData.GetAbTestLeg(), null, string.Empty, null, null);

            _signalService.Publish(new AnalyticSignal(data));
        }

        private bool TriggerAction(PopupAction popupAction, AdminDialogData adminDialogData)
        {
            if (popupAction == null) return false;

            if (popupAction.Type == AdminPopupActionType.ROOM ||
                popupAction.Type == AdminPopupActionType.SCRATCHERS_FREE ||
                popupAction.Type == AdminPopupActionType.SCRATCHERS_PAID ||
                popupAction.Type == AdminPopupActionType.SEND_GIFT ||
                popupAction.Type == AdminPopupActionType.ASK_GIFT ||
                popupAction.Type == AdminPopupActionType.VERIFY_EMAIL ||
                popupAction.Type == AdminPopupActionType.SO_CLOSE ||
                popupAction.Type == AdminPopupActionType.INVITE_FRIENDS ||
                popupAction.Type == AdminPopupActionType.OPEN_EVENT ||
                popupAction.Type == AdminPopupActionType.ACTION_TYPE_OFFER_WALL_ADS ||
                popupAction.Type == AdminPopupActionType.SCRATCHERS_PAID ||
                popupAction.Type == AdminPopupActionType.APP_MIGRATION_PROMO ||
                popupAction.Type == AdminPopupActionType.ENDLESS_SALE ||
                popupAction.Type == AdminPopupActionType.MYSTERY_BOX_SALE ||
                popupAction.Type == AdminPopupActionType.BOOSTER_SALE ||
                popupAction.Type == AdminPopupActionType.BASH_TOURNEY_TEASER)
            {
                return DoAction(popupAction, adminDialogData);
            }

            return false;
        }

        private void ShowShop(int shopType, string origin = "")
        {
            switch (shopType)
            {
                case 2:
                    _signalService.Publish(new OpenStoreSignal(StoreType.Coin, AdminPopupActionType.BUY_COINS));
                    break;
                case 3:
                    _signalService.Publish(new OpenStoreSignal(StoreType.PowerPlay));
                    break;
                case 4:
                    _signalService.Publish(_playerService.StartupFlags.IsExtraDaubShopV2
                        ? new OpenStoreSignal(StoreType.ExtraDaubV2, AdminPopupActionType.BUY_ROCKETS)
                        {
                            FromWhere = origin
                        }
                        : new OpenStoreSignal(StoreType.ExtraDaub, AdminPopupActionType.BUY_ROCKETS)
                        {
                            FromWhere = origin
                        });

                    break;
                default:
                    _signalService.Publish(new OpenStoreSignal(StoreType.Chips, AdminPopupActionType.BUY_CHIPS)
                    {
                        FromWhere = origin
                    });

                    break;
            }
        }

        private string GetOrigin(string triggerPointValue)
        {
            switch (triggerPointValue)
            {
                case "STARTUP":
                    return BingoConstants.ORIGIN_LOBBY;
                case "GAME_END":
                    return BingoConstants.ORIGIN_GAME_END;
                case "OOC_LOC_ADMIN_SALE":
                    return BingoConstants.ORIGIN_OOC_LOC;
                default:
                    return BingoConstants.ORIGIN_OTHERS;
            }
        }

        private class AdminPopupJob
        {
            public TypeName TriggerPoint { get; }
            public AdminPopupData PopupData { get; }

            public AdminPopupJob(TypeName triggerPoint, AdminPopupData popupData)
            {
                TriggerPoint = triggerPoint;
                PopupData = popupData;
            }
        }

        private void RemoveAdminPopupReachedMaxAllowedPurchase(AdminPopupData popupData)
        {
            _adminPopupModel.ComboSalePopups.RemoveAll(
                data => data.PopupId == popupData.PopupId);

            _salesBannerService.RemoveSaleBanner(true, popupData);
            if (popupData.WhatsHot == AdminPopupDataConstants.ADMIN_SALE)
                _signalService.Publish(new RemoveLobbyTileSignal
                {
                    LobbyId = BingoConstants.LOBBY_WHATS_HOT_ID,
                    Id = GameFeatureTypes.SALES
                });
        }
    }
}