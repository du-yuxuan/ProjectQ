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

using System;
using System.Collections.Generic;
using UnityEngine;
public enum LayoutType
{
    Top,
    Middle,
    Bottom,
}
public class PXR_PanelController : MonoBehaviour
{
    public float rotateAngle = 0f;
    public LayoutType layoutType = LayoutType.Middle;
    public float space = 0f;
    private readonly List<PXR_PanelItem> visiblePanels = new();
    void Start()
    {
        RecaculateAndRelayout();
    }
    private void OnValidate() {
        RecaculateAndRelayout();
    }
    public void RecaculateAndRelayout()
    {
        RecaculatePanel();
        Relayout();
    }
    private void Relayout()
    {
        int centerIndex = visiblePanels.Count / 2;
        int offset = 1;
        RectTransform preLeftRect = visiblePanels[centerIndex].GetComponent<RectTransform>();
        RectTransform preRightRect = visiblePanels[centerIndex].GetComponent<RectTransform>();
        while (centerIndex - offset >= 0 || centerIndex + offset < visiblePanels.Count)
        {
            if (centerIndex - offset >= 0)
            {
                var currentPanel = visiblePanels[centerIndex - offset].GetComponent<RectTransform>();
                LayoutTransform(ref currentPanel, preLeftRect, rotateAngle, -1f);
                preLeftRect = currentPanel;

            }
            if (centerIndex + offset < visiblePanels.Count)
            {
                var currentPanel = visiblePanels[centerIndex + offset].GetComponent<RectTransform>();
                LayoutTransform(ref currentPanel, preRightRect, rotateAngle);
                preRightRect = currentPanel;
            }
            offset++;
        }
    }
    private void LayoutTransform(ref RectTransform currentPanel,in RectTransform preRect,in float rotateAngle,in float direction = 1f)
    {
        float horizontalOffset = layoutType switch
        {
            LayoutType.Middle => 0,
            LayoutType.Top => preRect.rect.height * 0.5f - currentPanel.rect.height * 0.5f,
            LayoutType.Bottom => currentPanel.rect.height * 0.5f - preRect.rect.height * 0.5f,
            _ => 0f
        };
        float deep = currentPanel.rect.width * 0.5f * Mathf.Sin(rotateAngle * Mathf.Deg2Rad);
        currentPanel.localPosition = preRect.localPosition + (space + (preRect.rect.width + currentPanel.rect.width) * 0.5f) * direction * preRect.right - preRect.forward * deep + currentPanel.up * horizontalOffset;
        currentPanel.localEulerAngles = preRect.localEulerAngles + direction*rotateAngle * Vector3.up;
    }
    private void RecaculatePanel()
    {
        visiblePanels.Clear();
        PXR_PanelItem[] panelList = transform.GetComponentsInChildren<PXR_PanelItem>();
        foreach (var panelItem in panelList)
        {
            if (panelItem.gameObject.activeSelf && panelItem.TryGetComponent(out RectTransform itemRectTran))
            {
                visiblePanels.Add(panelItem);
            }
        }
    }
}
