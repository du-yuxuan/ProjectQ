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
using UnityEditor;
using UnityEngine.UI;
#if UNITY_EDITOR
public class PICOUICreator : MonoBehaviour
{
    private const string PICOUIPrafabsPath = "Packages/com.bytedance.pico.ui/Assets/Prefabs";
    private static void CreatePrefab(in MenuCommand menuCommand, string prefabPath, string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.Log($"Custom prefab {name} not found at '{prefabPath}'. Creating a default Slider instead.");
            return;
        }
        GameObject parent = menuCommand.context as GameObject;
        if (parent == null || parent.GetComponentInParent<Canvas>() == null)
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                parent = canvas.gameObject;
            }
            else
            {
                parent = new GameObject("Canvas");
                parent.AddComponent<Canvas>();
                parent.AddComponent<CanvasScaler>();
                parent.AddComponent<GraphicRaycaster>();
                Undo.RegisterCreatedObjectUndo(parent, "Create Canvas");
            }
        }
        GameObject uiPrefab = PrefabUtility.InstantiatePrefab(prefab, parent.transform) as GameObject;
        PrefabUtility.UnpackPrefabInstance(uiPrefab, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        uiPrefab.name = name;
        Undo.RegisterCreatedObjectUndo(uiPrefab, "Create " + uiPrefab.name);
        Selection.activeGameObject = uiPrefab;
        LayoutRebuilder.MarkLayoutForRebuild(uiPrefab.transform as RectTransform);
    }
    // Slider
    [MenuItem("GameObject/UI/PICO-UI/Slider/Stepless/Small Slider")]
    public static void CreateSmallSlider(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Slider/PICOSlider_Small_Stepless.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Stepless Small Slider");
    }
    [MenuItem("GameObject/UI/PICO-UI/Slider/Stepless/Regular Slider")]
    public static void CreateRegularSlider(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Slider/PICOSlider_Regular_Stepless.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Stepless Regular Slider");
    }
    [MenuItem("GameObject/UI/PICO-UI/Slider/Stepless/Regular Slider With Icon")]
    public static void CreateRegularWithIconSlider(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Slider/PICOSlider_Regular_Icon_Stepless.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Stepless Regular Slider With Icon");
    }
    [MenuItem("GameObject/UI/PICO-UI/Slider/Stepless/Max Slider")]
    public static void CreateMaxSlider(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Slider/PICOSlider_Max_Stepless.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Stepless Max Slider");
    }
    [MenuItem("GameObject/UI/PICO-UI/Slider/Stepless/Max Slider With Icon")]
    public static void CreateMaxWithIconSlider(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Slider/PICOSlider_Max_Icon_Stepless.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Stepless Max Slider With Icon");
    }
    [MenuItem("GameObject/UI/PICO-UI/Slider/Segment/Small Slider")]
    public static void CreateSmallSegmentSlider(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Slider/PICOSlider_Small_Segment.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Segment Small Slider");
    }
    [MenuItem("GameObject/UI/PICO-UI/Slider/Segment/Regular Slider")]
    public static void CreateRegularSegmentSlider(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Slider/PICOSlider_Regular_Segment.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Segment Regular Slider");
    }
    // List
    [MenuItem("GameObject/UI/PICO-UI/List/List Item")]
    public static void CreateListItem(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/List/PICOListItem.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] List Item");
    }
    // ToolBar
    [MenuItem("GameObject/UI/PICO-UI/ToolBar/ToolBar Icon")]
    public static void CreateToolBarIcon(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/ToolBar/PICOToolBar_Icon.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] ToolBar Icon");
    }
    [MenuItem("GameObject/UI/PICO-UI/ToolBar/ToolBar Word")]
    public static void CreateToolBarWord(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/ToolBar/PICOToolBar_Word.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] ToolBar Word");
    }
    // Side Navigation
    [MenuItem("GameObject/UI/PICO-UI/Side Navigation/Side Navigation")]
    public static void CreateSideNavigation(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/SideNavigation/PICOSideNavigation.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Side Navigation");
    }
    [MenuItem("GameObject/UI/PICO-UI/Side Navigation/Side Navigation Item")]
    public static void CreateSideNavigatioItem(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/SideNavigation/PICOSideNavigationItem.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Side Navigation Item");
    }
    // TabBar
    [MenuItem("GameObject/UI/PICO-UI/TabBar/PICOTabBar")]
    public static void CreateTabBar(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/TabBar/PICOTabBar_Vertical.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] PICOTabBar");
    }
    [MenuItem("GameObject/UI/PICO-UI/TabBar/PICOTabBar Item")]
    public static void CreateTabBarItem(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/TabBar/PICOTabBarItem.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] PICOTabBar Item");
    }
    [MenuItem("GameObject/UI/PICO-UI/Switch")]
    public static void CreateSwitch(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Switch/PICOSwitch.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] PICOSwitch");
    }
    [MenuItem("GameObject/UI/PICO-UI/Menu/Menu")]
    public static void CreateMenu(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Menu/Menu.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Menu");
    }
    [MenuItem("GameObject/UI/PICO-UI/Menu/MenuItem")]
    public static void CreateMenuItem(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/Menu/MenuItem.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] Menu Item");
    }
    [MenuItem("GameObject/UI/PICO-UI/Panel")]
    public static void CreatePanel(MenuCommand menuCommand)
    {
        string prefabPath = PICOUIPrafabsPath + "/PanelLayout/PICOPanels.prefab";
        CreatePrefab(in menuCommand, prefabPath, "[PICO UISet] PICOPanel");
    }
}
#endif