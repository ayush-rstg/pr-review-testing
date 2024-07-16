using System;
using System.Collections.Generic;
using Gsn.Bingo.Analytics;
using Gsn.Bingo.AppMonitor;
using Gsn.Bingo.Backend;
using Gsn.Bingo.BoosterSale;
using Gsn.Bingo.DoubleDlb;
using Gsn.Bingo.InAppPurchasing;
using Gsn.Bingo.MysteryBoxSale;
using Gsn.Bingo.Player;
using Gsn.Bingo.Rewards;
using Gsn.Bingo.Signals;
using Gsn.Bingo.Store.Service;
using Gsn.Bingo.Subscription;
using Gsn.Bingo.UnlimitedFreePlay;
using Gsn.Bingo.Utils;
using Newtonsoft.Json;
using UnityEngine.Purchasing;
using Zenject;

namespace Gsn.Bingo.AdminPopup
{
    public class OneTimePurchaseValidationService : IOneTimePurchaseValidationService, IInitializable, IResettable,
        IDisposable
    {
        private Func<TransactionData, RewardsDialogData> _getRewardDialogData;

        private ISignalService _signalService;
        private IPlayerService _playerService;
        private IPurchaseValidatorService _purchaseValidatorService;
        private IPlayerBalanceService _playerBalanceService;
        private IInAppPurchasingService _inAppPurchasingService;

        public ActiveSalesType CurrentActiveSalesType { get; set; }

        [Inject]
        public void Init(ISignalService signalService, IPlayerService playerService,
            IPurchaseValidatorService purchaseValidatorService, IPlayerBalanceService playerBalanceService,
            IInAppPurchasingService inAppPurchasingService)
        {
            _signalService = signalService;
            _playerService = playerService;
            _purchaseValidatorService = purchaseValidatorService;
            _playerBalanceService = playerBalanceService;
            _inAppPurchasingService = inAppPurchasingService;
        }

        public void Initialize()
        {
            _purchaseValidatorService.OnPurchaseValidated += OnPurchaseValidated;
            _inAppPurchasingService.PurchaseFailed += OnPurchaseFailedHandler;
            _inAppPurchasingService.PurchaseCanceled += OnPurchaseCanceledHandler;
        }

        public void Reset()
        {
            CurrentActiveSalesType = ActiveSalesType.Default;
            ClearMethodReferences();
        }

        public void Dispose()
        {
            _inAppPurchasingService.PurchaseFailed -= OnPurchaseFailedHandler;
            _inAppPurchasingService.PurchaseCanceled -= OnPurchaseCanceledHandler;
            Reset();
        }

        public void SetMethodReferences(Func<TransactionData, RewardsDialogData> getRewardsDialogData)
        {
            _getRewardDialogData = getRewardsDialogData;
        }

        public void ClearMethodReferences()
        {
            _getRewardDialogData = null;
        }

        private void OnPurchaseValidated(bool result, IStoreTransactionModel transactionModel,
            TransactionData transactionData)
        {
            var isOneTimePurchase = transactionData?.IsOneTimePurchase ?? false;
            Log.Info(LogChannels.InAppPurchasing,
                $"On Purchase Validated Is One Time Purchase: {transactionData?.IsOneTimePurchase}");

            if (!result || !isOneTimePurchase)
            {
                Log.Info(LogChannels.InAppPurchasing,
                    $"Skipping one time purchase validation for product: {transactionData?.ProductId}");

                return;
            }

            HandleOneTimePurchaseSaleReward(transactionData);
        }

        private void HandleOneTimePurchaseSaleReward(TransactionData transactionData)
        {
            var rewardsDialogData = _getRewardDialogData?.Invoke(transactionData) ?? new RewardsDialogData()
            {
                RewardData = transactionData.Reward,
                BonusRewards = transactionData.BonusRewards
            };

            if (transactionData.Reward.HasDoubleDlbReward)
            {
                HandleDoubleDlbSaleReward(transactionData, rewardsDialogData);
            }
            else if (transactionData.Reward.HasSubscriptionReward)
            {
                HandleSubscriptionSaleReward(transactionData, rewardsDialogData);
            }
            else if (transactionData.Reward.HasUnlimitedFreePlayReward)
            {
                HandleUnlimitedFreePlayReward(transactionData, rewardsDialogData.DescriptionMessage ?? string.Empty);
            }
            else if (transactionData.HasMysteryRewards)
            {
                HandleMysteryBoxSaleReward(transactionData);
            }
            else if (transactionData.HasBoosterSaleReward)
            {
                HandleBoosterSaleReward(transactionData);
            }

            CurrentActiveSalesType = ActiveSalesType.Default;
        }

        private void HandleDoubleDlbSaleReward(TransactionData transactionData, RewardsDialogData rewardsDialogData)
        {
            var doubleDlbReward = transactionData.Reward.DoubleDlbRewardData;
            Log.Info(LogChannels.Client, $"[ double dlb ] OnPurchaseValidated - {doubleDlbReward}");
            _signalService.Publish<OneTimePurchaseCompletedSignal>();

            var doubleDlbRewardData =
                JsonConvert.DeserializeObject<DoubleDlbRewardData>(JsonConvert.SerializeObject(doubleDlbReward));

            var analyticshowPopupData = new AnalyticEventFieldModel(AnalyticDoubleDlbConstant.DDLB,
                EventCodeConst.SCREEN, EventSubCodeConst.SHOW);

            analyticshowPopupData.ScreenId = AnalyticsPopupIdConst.DOUBLE_DLB_SCREEN;
            analyticshowPopupData.ScreenName = AnalyticDoubleDlbConstant.ActivationScreenName;

            rewardsDialogData.TitleMessage = "M_632";
            rewardsDialogData.BodyMessage = "M_5536";
            rewardsDialogData.DescriptionMessage = doubleDlbRewardData.ConfirmationText;
            rewardsDialogData.AnalyticPopupShowData = analyticshowPopupData;
            rewardsDialogData.ShowPlus = true;
            rewardsDialogData.ActionButtonLabel = "Awesome";
            rewardsDialogData.OnContinue += () =>
            {
                _signalService.Publish(new InitDoubleDlbSignal(doubleDlbRewardData));
            };

            _signalService.Publish(new ShowRewardsPopupSignal { Data = rewardsDialogData });
        }

        private void HandleSubscriptionSaleReward(TransactionData transactionData, RewardsDialogData rewardsDialogData)
        {
            var subscriptionReward = transactionData.Reward.SubscriptionRewardData;
            _signalService.Publish<OneTimePurchaseCompletedSignal>();

            var subscriptionInfo =
                JsonConvert.DeserializeObject<SubscriptionRewardInfo>(
                    JsonConvert.SerializeObject(subscriptionReward));

            subscriptionInfo.Chips = transactionData.Reward.Chips;

            var analyticPopupData = new AnalyticEventFieldModel(AnalyticsSubscriptionConstants.BStash,
                EventCodeConst.SCREEN, EventSubCodeConst.SHOW);

            analyticPopupData.ScreenId = AnalyticsPopupIdConst.BASH_STASH_ACTIVATION_POPUP;
            analyticPopupData.ScreenName = AnalyticsSubscriptionConstants.ActivationScreenName;

            if (rewardsDialogData.DescriptionMessage.IsNullOrEmpty())
            {
                rewardsDialogData.DescriptionMessage = "M_6137";
            }

            rewardsDialogData.AnalyticPopupShowData = analyticPopupData;
            rewardsDialogData.ActionButtonLabel = "Awesome";
            rewardsDialogData.OnContinue += () =>
            {
                _signalService.Publish(new InitSubscriptionPackSignal(subscriptionInfo));
            };

            _signalService.Publish(new ShowRewardsPopupSignal { Data = rewardsDialogData });
        }

        private void HandleUnlimitedFreePlayReward(TransactionData transactionData, string descriptionMessage)
        {
            var unlimitedFreePlayReward = transactionData.Reward.UnlimitedFreePlayData;

            Log.Info(LogChannels.Client, "UNLIMITED_FREE_PLAY", $"OnPurchaseValidated - {unlimitedFreePlayReward}");

            _signalService.Publish<OneTimePurchaseCompletedSignal>();

            var otherData = new Dictionary<string, object>()
            {
                { "time_duration", unlimitedFreePlayReward }
            };

            _playerService.IsUnlimitedFreePlayPurchased = true;
            _playerService.IsUnlimitedFreePlayActiveState = true;
            _playerService.UnlimitedFreePlayRewardDescriptionText = descriptionMessage;

            _signalService.Publish(new InitUnlimitedFreePlaySignal(unlimitedFreePlayReward));
            _signalService.Publish(new ShowUnlimitedFreePlayRewardPopupSignal(unlimitedFreePlayReward));

            var analyticPopupData = new AnalyticEventFieldModel(
                AnalyticsUnlimitedFreePlayConstants.UnlimitedFreePlay, EventCodeConst.TIMER,
                EventSubCodeConst.START);

            analyticPopupData.OtherDataJson = JsonConvert.SerializeObject(otherData);
            _signalService.Publish(new AnalyticSignal(analyticPopupData));
        }

        private void HandleMysteryBoxSaleReward(TransactionData transactionData)
        {
            switch (CurrentActiveSalesType)
            {
                case ActiveSalesType.Default:
                    _playerBalanceService.AwardChips(transactionData.Reward.Chips);
                    _playerBalanceService.AwardChips(transactionData.Reward.ExtraRewardData.MysteryBoxRewards.Chips);
                    break;
                case ActiveSalesType.MysteryBoxSale:
                    _signalService.Publish(new MysteryBoxSaleRewardChipsSignal
                    {
                        NormalChips = transactionData.Reward.Chips,
                        MysteryChips = transactionData.Reward.ExtraRewardData.MysteryBoxRewards.Chips
                    });

                    break;
            }
        }

        private void HandleBoosterSaleReward(TransactionData transactionData)
        {
            switch (CurrentActiveSalesType)
            {
                case ActiveSalesType.Default:
                    _signalService.Publish(new BoosterSaleUpdateGoldenTicketSignal
                    {
                        RewardData = transactionData.Reward,
                        GoldenTickets = transactionData.Reward.ExtraRewardData.BoosterSaleReward.GoldenStamps
                    });

                    break;
                case ActiveSalesType.BoosterSale:
                    _signalService.Publish(new BoosterSaleRewardSignal
                    {
                        RewardData = transactionData.Reward,
                        GoldenTickets = transactionData.Reward.ExtraRewardData.BoosterSaleReward.GoldenStamps
                    });

                    break;
            }
        }

        private void OnPurchaseFailedHandler(Product product, PurchaseFailureCustomReason reason, string failureMessage)
        {
            CurrentActiveSalesType = ActiveSalesType.Default;
        }

        private void OnPurchaseCanceledHandler(Product product)
        {
            CurrentActiveSalesType = ActiveSalesType.Default;
        }
    }

    public enum ActiveSalesType
    {
        Default,
        MysteryBoxSale,
        BoosterSale
    }
}