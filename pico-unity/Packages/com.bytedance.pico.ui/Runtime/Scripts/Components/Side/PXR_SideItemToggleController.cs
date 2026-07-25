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

public class PXR_SideItemToggleController : MonoBehaviour
{
    public GameObject group;
    public Image icon;
    public Toggle toggle;
    public void Toggle()
    {
        if (group.transform.childCount <= 0) return;
        group.SetActive(toggle.isOn);
        if (icon != null) icon.transform.eulerAngles = toggle.isOn ? Vector3.back * 90f : Vector3.zero;
        Transform p = group.transform.parent;
        while (p != null && p.TryGetComponent(out RectTransform rect))
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            p = p.parent;
        }
    }
}
