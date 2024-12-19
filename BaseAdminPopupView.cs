using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Gsn.Bingo.Dialogs;
using Gsn.Bingo.FishMeta;
using Gsn.Bingo.Utils;
using Gsn.Bingo.VideoPlayer;
using Newtonsoft.Json;
using TMPro;
using UniRx;
using UnityEngine;

namespace Gsn.Bingo.AdminPopup
{
    public abstract class BaseAdminPopupView : BaseDialogView<AdminDialogData>
    {
        [SerializeField] private TextMeshProUGUI _titleTf;
        [SerializeField] private TextMeshProUGUI _bodyTf;
        [SerializeField] private TextMeshProUGUI _timerTf;
        [SerializeField] private List<AdminPopupTextView> _textList;
        [SerializeField] private List<AdminPopupTextureView> _texturelist;
        [SerializeField] private List<AdminPopupButtonView> _actionButtonList;
        [SerializeField] private List<AdminPopupGameObjectView> _itemList;
        [SerializeField] private List<BaseAdminPopupAnimView> _animationList;
        [SerializeField] private AdminPopupProgressBarView _progressBar;
        [SerializeField] private string _soundSpecialOpening;
        [SerializeField] private VideoPlayerView _videoPlayerView;
        [SerializeField] private int _newIntAdded;
#if UNITY_EDITOR
        public List<AdminPopupTextView> TextList
        {
            get => _textList;
            set => _textList = value;
        }

        public TextMeshProUGUI TitleTf
        {
            get => _titleTf;
            set => _titleTf = value;
        }

        public List<AdminPopupButtonView> ActionButtonList
        {
            get => _actionButtonList;
            set => _actionButtonList = value;
        }

        public TextMeshProUGUI BodyTf
        {
            get => _bodyTf;
            set => _bodyTf = value;
        }
#endif

        public event Action<PopupAction> OnPopupAction;
        public event Action<AnimCallBackData, Action<AdminOtherData>> OnAnimActionCallBack;
        public event Action<bool> StartTimerTick;
        public event Action<Dictionary<string, object>> VideoActionAnalyticCallback;
        public event Action<int, AdminGameObjectData> InteractableListViewClickAction;

        //TODO : Please remove the seconds remaining and use end time
        public long SecondsRemaining => _secondsRemaining;
        public List<AdminPopupGameObjectView> ItemList => _itemList;
        public string PopupId => _popupData?.PopupId;
        public string PopupTitle => _popupData?.Title;
        public Rules PopupRules => _popupData?.Rules;
        public bool IsFlashSales => _popupData != null && _popupData.IsFlashSale;
        public AdminPopupData PopupData => _popupData;

        private Action<UserFish, AdminPopupGameObjectView> _setFishSpriteCallback;
        private List<AdminAnimationData> _sequenceAnimations;
        private CompositeDisposable _disposable;
        private Func<long> _getCurrentGmtTimeFunc;
        private AdminPopupData _popupData;
        private long _secondsRemaining;
        private long _endTime;
        private int _currentAnimSeqID;

        public override void SetData(AdminDialogData showData)
        {
            base.SetData(showData);

            if (Data == null)
                return;

            _disposable = new CompositeDisposable();
            _popupData = Data.PopupData;
            ConstructUI();

            if (_soundSpecialOpening.IsNotNullOrEmpty())
                PlaySound(_soundSpecialOpening);
        }

        public override void OnButtonCloseClick()
        {
            OnPopupAction?.Invoke(null);
        }

        public override void CloseDialog(Action onClose = null)
        {
            if (_videoPlayerView != null)
            {
                _videoPlayerView.ClearRenderTexture();
            }

            DisableButtons();

            Clear();

            base.CloseDialog(onClose);
        }

        protected virtual void SetTimer(long secondsRemaining)
        {
            _endTime = secondsRemaining + _getCurrentGmtTimeFunc.Invoke();
            _secondsRemaining = secondsRemaining;
            if (_secondsRemaining <= 0) return;

            const int dayInSecs = 86400;
            if (_secondsRemaining < dayInSecs)
            {
                StartTimerTick?.Invoke(true);
            }
            else
            {
                SetTimerText(_secondsRemaining);
            }
        }

        protected virtual void OnTimerEnds()
        {
            SetTimerText(0);
            if (Data.PopupData.CanSkipAutoCloseOnTimerEnd) return;

            //TODO : Remove if any combo sale banner or btn from lobby related to this popup.
            OnPopupAction?.Invoke(null);
        }

        protected void OnActionClick(PopupAction clickedAction)
        {
            Log.Info(LogChannels.AdminPopup,
                $"<color=#ffff00ff>AdminPopupView :: Action Clicked = {clickedAction.Type} </color>");

            OnPopupAction?.Invoke(clickedAction);
        }

        public string GetAbTestName()
        {
            return PopupData == null ? string.Empty : PopupData.GetAbTestName();
        }

        public string GetAbTestLeg()
        {
            return PopupData == null ? string.Empty : PopupData.GetAbTestLeg();
        }

        public void SetCurrentGmtTimeFunc(Func<long> getCurrentGmtTimeFunc)
        {
            _getCurrentGmtTimeFunc = getCurrentGmtTimeFunc;
        }

        public void OnTimerTick(long time)
        {
            _secondsRemaining = _endTime - time;

            if (_secondsRemaining <= 0)
            {
                StartTimerTick?.Invoke(false);
                OnTimerEnds();
            }
            else
            {
                SetTimerText(_secondsRemaining);
            }
        }

        public void UpdateDynamicInfoAssets(List<FishAssetData<Sprite>> loadedFish)
        {
            foreach (var item in _itemList)
            {
                if (!item.HasDynamicFishInfoIcon) continue;

                var fishAsset = loadedFish.Find(data =>
                    data.FishId == (int.TryParse(item.InfoAssetPath, out var fishId) ? fishId : 0));

                if (fishAsset != null && fishAsset.Asset != null)
                    item.DynamicImage.sprite = fishAsset.Asset;
            }
        }

        public void UpdateDynamicRewardImages(List<FishAssetData<Sprite>> loadedFish)
        {
            var gifts = PopupData.Gifts;
            if (gifts?.MetaFishRewards == null) return;

            foreach (var item in ItemList)
            {
                if (!item.HasFishReward || gifts.MetaFishRewards.Count <= 0) continue;

                var fishAsset = loadedFish.Find(data =>
                    data.FishId == (int.TryParse(item.InfoAssetPath, out var fishId) ? fishId : 0));

                if (fishAsset != null && fishAsset.Asset != null)
                    item.DynamicImage.sprite = fishAsset.Asset;
            }
        }

        public void DisableButtons()
        {
            foreach (var element in _actionButtonList)
            {
                if (element != null)
                    element.CleanUp();
            }

            if (_buttonClose != null)
                _buttonClose.interactable = false;
        }

        public Dictionary<string, object> GetCumulativeVideoAnalyticsData()
        {
            return _videoPlayerView != null ? _videoPlayerView.GetCumulativeAnalyticsData() : null;
        }

        public void UpdateVideoPlayerEvents(bool val)
        {
            if (_videoPlayerView == null) return;

            if (val)
            {
                _videoPlayerView.SetButtonsInteractable += SetButtonsInteractable;
            }
            else
            {
                _videoPlayerView.SetButtonsInteractable -= SetButtonsInteractable;
            }
        }

        public void SetImageByKey(Texture2D texture2D, string key)
        {
            if (texture2D == null || _texturelist.Count == 0)
                return;

            var image = _texturelist.Find(view => view != null && key == view.Key);
            if (image == null)
            {
                Log.Info(LogChannels.AdminPopup,
                    $"Logo Feature: Cannot find image from the texture list with the key {key}");

                return;
            }

            image.SetData(texture2D);
        }

        public void UpdateProfileImage(string userId)
        {
            if (_itemList == null || _itemList.Count == 0) return;

            var adminPopupGameObjectView = _itemList.Find(item => item.AdminGameObjectData?.UserId == userId);
            if (adminPopupGameObjectView == null) return;

            adminPopupGameObjectView.LoadProfileImage();
        }

        private void SetButtonsInteractable(bool val)
        {
            foreach (var element in _actionButtonList)
            {
                if (element != null)
                    element.SetInteractable(val);
            }

            if (_buttonClose != null)
                _buttonClose.interactable = val;
        }

        public void ToggleButtonInteraction(PopupAction action, bool val)
        {
            if (_popupData.PopupActions == null) return;

            var index = _popupData.PopupActions.IndexOf(action);
            if (index == -1 || index >= _actionButtonList.Count || _actionButtonList[index] == null) return;

            _actionButtonList[index].SetInteractable(val);
        }

        public void ToggleCloseButtonInteraction(bool val)
        {
            if (_buttonClose != null)
            {
                _buttonClose.interactable = val;
            }
        }

        protected List<AdminPopupButtonView> GetButtonList()
        {
            return _actionButtonList;
        }

        private void UpdateFishAsset(AdminPopupGameObjectView viewObj, UserFish fishGift)
        {
            _setFishSpriteCallback?.Invoke(fishGift, viewObj);
        }

        private void SetTimerText(long saleEndTime)
        {
            if (_timerTf != null)
                _timerTf.text = FormattingTools.FormatTime(saleEndTime);
        }

        private void SetTitleText(string popupDataTitle)
        {
            if (_titleTf != null && popupDataTitle.IsNotNullOrEmpty())
                _titleTf.text = popupDataTitle;
        }

        private void SetBodyText(string popupDataMessage)
        {
            if (_bodyTf != null && popupDataMessage.IsNotNullOrEmpty())
                _bodyTf.text = popupDataMessage;
        }

        private void DisableAllAnimators()
        {
            foreach (var animView in _animationList)
            {
                animView.gameObject.SetActive(false);
            }
        }

        private void SetTexts(Dictionary<string, string> popupDataTexts)
        {
            if (popupDataTexts == null) return;

            var textLog = new StringBuilder();
            textLog.AppendLine($"Popup ID : {Data.PopupData.PopupId}, Text");
            foreach (var textData in popupDataTexts)
            {
                if (textData.Value == null || textData.Key.IsNullOrEmpty())
                {
                    textLog.AppendLine("Text data null!");
                    continue;
                }

                var textView = _textList.Find(view => view != null && "text" + view.Key == textData.Key);
                if (textView != null && textData.Value.IsNotNullOrEmpty())
                {
                    textView.SetData(textData.Value);
                    textLog.AppendLine($"Text_{textView.Key} = {textData.Value}");
                }
                else
                {
                    textLog.AppendLine($"Text_{textData.Key} asset missing!");
                }
            }

            Log.Info(LogChannels.AdminPopup, "BASE_ADMIN_POPUP_VIEW", textLog.ToString());
        }

        private void SetButtons(IEnumerable<PopupAction> popupActions)
        {
            var counter = 0;
            foreach (var popupAction in popupActions)
            {
                if (counter < _actionButtonList.Count)
                {
                    var buttonView = _actionButtonList[counter];
                    if (buttonView != null && popupAction != null)
                    {
                        buttonView.SetData(popupAction, OnActionClick);
                    }
                }

                counter++;
            }
        }

        private void SetListData(Dictionary<string, AdminGameObjectData> itemsData)
        {
            if (itemsData == null) return;

            var listLog = new StringBuilder();
            listLog.AppendLine($"Popup ID : {Data.PopupData.PopupId}, List");
            foreach (var itemData in itemsData)
            {
                if (itemData.Value == null || itemData.Key.IsNullOrEmpty())
                {
                    listLog.AppendLine("List data null!");
                    continue;
                }

                var itemView = _itemList.Find(view => view != null && "list" + view.Key == itemData.Key);
                if (itemView != null)
                {
                    itemView.SetData(itemData.Value);
                    listLog.AppendLine($"List_{itemView.Key}, State = {itemData.Value.State}");

                    if (itemView is AdminPopupInteractableListView adminPopupInteractableListView)
                    {
                        adminPopupInteractableListView.ClickAction += InteractableListViewClickAction;
                    }
                }
                else
                {
                    listLog.AppendLine($"List_{itemData.Key} asset missing!");
                }
            }

            Log.Info(LogChannels.AdminPopup, "BASE_ADMIN_POPUP_VIEW", listLog.ToString());
        }

        private void SetChallengeEventData(AdminOtherData othersData)
        {
            if (othersData == null) return;

            SetProgressBar(othersData.ProgressBarData);

            SetAnimations(othersData.AnimationSetData);
        }

        private void SetProgressBar(AdminProgressBarData progressBarData)
        {
            if (_progressBar != null)
                _progressBar.SetData(progressBarData);
        }

        private void SetAnimations(AdminAnimationSet animationSetData)
        {
            if (animationSetData == null) return;

            if (animationSetData.NonSequenceAnimations != null)
            {
                foreach (var animationData in animationSetData.NonSequenceAnimations)
                {
                    var animView = _animationList.Find(x => "anim" + x.Key == animationData.InstanceName);
                    if (animView == null) continue;

                    animView.gameObject.SetActive(true);
                    animView.SetData(animationData);
                    animView.PlaySound += PlaySound;
                    animView.StopSound += StopSound;
                }
            }

            if (animationSetData.SequenceAnimations == null || animationSetData.SequenceAnimations.Count <= 0) return;

            _sequenceAnimations = animationSetData.SequenceAnimations;
            _currentAnimSeqID = 0;
            PlayAnimationWithDelay(_sequenceAnimations[_currentAnimSeqID]);
        }

        private void PlayAnimationWithDelay(AdminAnimationData animData)
        {
            if (animData == null) return;

            if (animData.Delay > 0)
            {
                Observable.Timer(TimeSpan.FromSeconds(animData.Delay))
                    .Subscribe(_ => PlayAnimationSequence(animData)).AddTo(_disposable);
            }
            else
            {
                PlayAnimationSequence(animData);
            }
        }

        private void PlayAnimationSequence(AdminAnimationData animData)
        {
            var animView = _animationList.Find(x => x.Key == animData.InstanceName);
            if (animView == null) return;

            animView.gameObject.SetActive(true);

            if (animData.CanUpdateBeforeAnim)
                DoAnimationAction(animData.AnimActionData);

            animView.SetData(animData, OnAnimSequenceComplete);
        }

        private void OnAnimSequenceComplete(AdminAnimationData animData)
        {
            if (!animData.CanUpdateBeforeAnim)
                DoAnimationAction(animData.AnimActionData);

            PlayAnimationWithDelay(_sequenceAnimations[++_currentAnimSeqID]);

            if (animData.AnimCallBackData != null)
                OnAnimActionCallBack?.Invoke(animData.AnimCallBackData, OnAnimActionServerResponse);
        }

        private void OnAnimActionServerResponse(AdminOtherData otherData)
        {
            SetAnimations(otherData?.AnimationSetData);
        }

        private void DoAnimationAction(AnimActionData animActionData)
        {
            if (animActionData == null) return;

            SetListData(animActionData.GameObjectData);
            SetTexts(animActionData.Texts);
            if (_bodyTf != null && animActionData.Message.IsNotNullOrEmpty())
                _bodyTf.text = animActionData.Message;
        }

        private void ConstructUI()
        {
            DisableAllAnimators();
            SetTimer(_popupData.SecondsRemaining - (_getCurrentGmtTimeFunc.Invoke() - _popupData.StartTime));
            SetTitleText(_popupData.Title);
            SetBodyText(_popupData.Message);

            SetListData(_popupData.Others?.GameObjectData);
            SetChallengeEventData(_popupData.Others);
            SetTexts(_popupData.Texts);
            SetButtons(_popupData.PopupActions);
            SetUpCloseBtn(_popupData.CloseBtnState);
            SetupVideoPlayer();
        }

        private void SetupVideoPlayer()
        {
            if (_videoPlayerView == null || _popupData.VideoUrl.IsNullOrEmpty()) return;

            var settings = new VideoPlayerSettings();
            var settingsData = _popupData.VideoSettings;
            if (settingsData.IsNotNullOrEmpty())
            {
                try
                {
                    settings = JsonConvert.DeserializeObject<VideoPlayerSettings>(settingsData);
                }
                catch (Exception exception)
                {
                    Log.Info(LogChannels.AdminPopup, $"Video Settings exception: {exception.Message}");
                }
            }

            _videoPlayerView.SetData(_popupData.VideoUrl, settings,
                VideoActionAnalyticCallback);
        }

        private void SetUpCloseBtn(int state)
        {
            if (state == -1) return;

            ToggleCloseButtonInteraction(state == 1);
        }

        private void Clear()
        {
            if (_animationList == null || _animationList.Count <= 0) return;

            foreach (var animView in _animationList.Where(animView => animView != null))
            {
                animView.PlaySound -= PlaySound;
                animView.StopSound -= StopSound;
            }
        }
    }
}
