/*******************************************************************************
Copyright © 2015-2022 PICO Technology Co., Ltd.All rights reserved.  

NOTICE：All information contained herein is, and remains the property of 
PICO Technology Co., Ltd. The intellectual and technical concepts 
contained herein are proprietary to PICO Technology Co., Ltd. and may be 
covered by patents, patents in process, and are protected by trade secret or 
copyright law. Dissemination of this information or reproduction of this 
material is strictly forbidden unless prior written permission is obtained from
PICO Technology Co., Ltd. 
*******************************************************************************/
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(Slider))]
public class PXR_SliderVisualController : PXR_UIInputHandler
{
    [Serializable]
    public struct IconInfo
    {
        public Sprite icon;
        [Range(0, 1)] public float scope;
    }
    public Slider slider;
    [Range(0, 1)] public float scope;
    public Image fillImage;
    public List<IconInfo> iconInfoList;
    public GameObject handle;
    private readonly bool isMultiIcon = false;
    private bool isHover = false;
    public bool canHideHandle;
    private bool IsShowHandle => !canHideHandle || isHover;
    private bool IsMultiIcon => iconInfoList != null && iconInfoList.Count > 1;

    void Awake()
    {
        if (slider == null) slider = GetComponent<Slider>();
    }
    void Start()
    {
        VisuallyHander();
        iconInfoList = iconInfoList.OrderBy(i => i.scope).ToList();
    }
    void Update()
    {
        VisuallyHander();
    }
    private void OnValidate()
    {
        if (fillImage != null)
        {
            var isShowIcon = iconInfoList != null && iconInfoList.Count > 0;
            fillImage.gameObject.SetActive(isShowIcon);
            fillImage.sprite = isShowIcon?iconInfoList[0].icon:null;
        }
    }
    private void VisuallyHander()
    {
        HandleHandler();
        IconHandler();
    }
    private void HandleHandler()
    {
        if (handle == null) return;
        handle.SetActive(slider.value > scope && IsShowHandle);
    }
    public override void OnPointerExit(PointerEventData eventData)
    {
        isHover = false;
    }
    public override void OnPointerEnter(PointerEventData eventData)
    {
        isHover = true;
    }
    private void IconHandler()
    {
        if (IsMultiIcon && fillImage != null)
        {
            for (var i = 0; i < iconInfoList.Count; i++)
            {
                if (slider.value <= iconInfoList[i].scope)
                {
                    if (iconInfoList[i].icon == null) Debug.LogWarning("There is no configuration icon, Please ensure that all ICONS in the list have been configured; otherwise, the effect may not meet expectations");
                    fillImage.sprite = iconInfoList[i].icon;
                    return;
                }
            }
        }
    }
}
