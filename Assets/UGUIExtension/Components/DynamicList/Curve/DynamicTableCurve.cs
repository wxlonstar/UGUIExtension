﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 曲线动态列表
/// </summary>
public class DynamicTableCurve : MonoBehaviour, IDragHandler, IBeginDragHandler, IEndDragHandler, ICanvasRaycastFilter
{
    #region 基础属性
    /// <summary>
    /// 方向
    /// </summary>
    public LayoutRule.Direction Direction = LayoutRule.Direction.Horizontal;
    /// <summary>
    /// grid事件
    /// </summary>
    public Action<int, DynamicGrid> DynamicTableGridDelegate;
    /// <summary>
    /// 缩放曲线
    /// </summary>
    public AnimationCurve ScaleCurve;
    /// <summary>
    /// 位置曲线
    /// </summary>
    public AnimationCurve PositionCurve;
    /// <summary>
    /// 深度曲线
    /// </summary>
    public AnimationCurve DepthCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 1), new Keyframe(1, 0));

    /// <summary>
    /// 可视区域
    /// </summary>
    public Vector2 ViewSize = Vector2.zero;

    /// <summary>
    /// 总数
    /// </summary>
    public int TotalCount = 0;

    /// <summary>
    /// 内容节点
    /// </summary>
    public RectTransform Content;

    /// <summary>
    /// 视野节点
    /// </summary>
    public RectTransform Viewport;

    /// <summary>
    /// 动态节点
    /// </summary>
    public DynamicGrid Grid;

    /// <summary>
    /// 使用中的节点
    /// </summary>
    protected HashSet<DynamicGrid> UsingGridSet = new HashSet<DynamicGrid>();

    /// <summary>
    /// 准备回收
    /// </summary>
    protected Stack<DynamicGrid> PreRecycleGridStack = new Stack<DynamicGrid>();

    /// <summary>
    /// 缓存列表
    /// </summary>
    protected Stack<DynamicGrid> GridPoolStack = new Stack<DynamicGrid>();

    /// <summary>
    /// 间隔因子，影响滑动速度
    /// </summary>
    public float InteralFactor = 0.1f;

    /// <summary>
    /// 聚焦点索引
    /// </summary>
    public int StartIndex = 0;

    /// <summary>
    /// 第几个是中心，影响显示个数
    /// </summary>
    public int CentralIndex = 3;

    /// <summary>
    /// 当前偏移
    /// </summary>
    protected float CurOffsetValue = 0.5f;

    /// <summary>
    /// 总偏移
    /// </summary>
    protected float TotalOffsetValue;

    /// <summary>
    /// 是否为Gird添加点击事件
    /// </summary>
    public bool IsGridTouchEventEnable = true;

    /// <summary>
    /// 副轴偏移
    /// </summary>
    public float AxisOffset = 0.0f;


    /// <summary>
    /// 是否需要修正
    /// </summary>
    public bool IsNeedTweenToFix = true;
    /// <summary>
    /// 修正偏移
    /// </summary>
    private float FixOffsetValue;

    /// <summary>
    /// 修正开始位置
    /// </summary>
    private float TweenStartOffsetValue = 0.0f;

    /// <summary>
    /// 修正时间
    /// </summary>
    public float LerpDuration = 0.2f;

    /// <summary>
    /// 当前时间
    /// </summary>
    private float CurrentDuration = 0.0f;

    /// <summary>
    /// 对齐中
    /// </summary>
    private bool IsTweening = false;

    /// <summary>
    /// RectTransform
    /// </summary>
    private RectTransform m_Rect;

    private RectTransform rectTransform
    {
        get
        {
            if (m_Rect == null)
                m_Rect = GetComponent<RectTransform>();
            return m_Rect;
        }
    }

    /// <summary>
    /// 拖拽中
    /// </summary>
    private bool IsDragging;

    /// <summary>
    /// 是否加载完成
    /// </summary>
    private bool IsInitCompeleted = false;


    /// <summary>
    /// 使用惯性
    /// </summary>
    public bool Inertia = true;

    /// <summary>
    /// 速度
    /// </summary>
    private Vector2 Velocity;

    /// <summary>
    /// 惯性系数 Only used when inertia is enabled
    /// </summary>
    public float DecelerationRate = 0.135f;

    /// <summary>
    /// 上一帧光标滑动的位置
    /// </summary>
    protected Vector2 LastCursorStartPosition = Vector2.zero;

    /// <summary>
    /// 上一次光标位置
    /// </summary>
    protected Vector2 PreCursorPosition;

    #endregion

    void Awake()
    {
        Init();
    }

    void Init()
    {
        if (!IsActive())
            return;

        SetViewSize(rectTransform.rect.size);

        Content.anchoredPosition = Vector2.zero;
        Content.anchorMax = new Vector2(1, 1);
        Content.anchorMin = new Vector2(0, 0);
        Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rectTransform.rect.size.x);
        Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rectTransform.rect.size.y);
    }


    /// <summary>
    /// 是否激活
    /// </summary>
    /// <returns></returns>
    protected bool IsActive()
    {
        if (!isActiveAndEnabled)
            return false;

        if (Content == null || Viewport == null)
            return false;

        return true;
    }
    /// <summary>
    /// 设置可视区域
    /// </summary>
    /// <param name="size"></param>
    public virtual void SetViewSize(Vector2 size)
    {
        ViewSize = size;

        if (Viewport == null)
            return;

        Viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
        Viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
    }

    /// <summary>
    /// 总的偏移
    /// </summary>
    void CalculateTotalOffsetValue()
    {
        TotalOffsetValue = InteralFactor * (TotalCount - 1);
    }

    /// <summary>
    /// 重载数据
    /// </summary>
    /// <param name="startIndex">起始索引</param>
    public void ReloadData(int startIndex = -1)
    {
        if (!IsActive())
            return;

        //停止惯性
        StopMovement();
        //重置索引
        ResetStartIndex(startIndex);
        //计算总的偏移值
        CalculateTotalOffsetValue();
        //重載
        ReloadGrids();
    }


    /// <summary>
    /// 重载Grids
    /// </summary>
    public void ReloadGrids()
    {
        //回收
        foreach (var grid in UsingGridSet)
        {
            if (grid == null)
                continue;
            RecycleTableGrid(grid);
        }

        UsingGridSet.Clear();


        //防止超出
        int count = StartIndex - CentralIndex + GetShowingCount();

        for (int i = 0; i <= count; i++)
        {
            if (StartIndex < CentralIndex)
                TableGridAtIndex(i);
            else
                TableGridAtIndex(i + StartIndex - CentralIndex);
        }

        IsInitCompeleted = true;
    }



    /// <summary>
    /// 根据Index更新Grid
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    protected virtual DynamicGrid TableGridAtIndex(int index)
    {
        if (index < 0 || index >= TotalCount)
            return null;

        DynamicGrid grid = LoadGridFormPool();

        if (grid == null)
            Debug.LogErrorFormat("DynamicTableCurve load grid at index {0} fail!", index);

        grid.Index = index;
        UsingGridSet.Add(grid);


        OnTableGridAtIndex(grid);

        if (!grid.gameObject.activeSelf)
            grid.gameObject.SetActive(true);


        //设置位置
        SetGridsAlongAxis((int)Direction, grid, index);

        return grid;
    }

    /// <summary>
    /// 重置索引
    /// </summary>
    /// <param name="startIndex"></param>
    void ResetStartIndex(int startIndex)
    {
        StartIndex = Mathf.Clamp(startIndex, 0, TotalCount - 1);
        CurOffsetValue = StartIndex * InteralFactor;
        FixOffsetValue = CurOffsetValue;
    }

    void StopMovement()
    {
        Velocity = Vector2.zero;
    }


    /// <summary>
    /// 设置单个cell位置
    /// </summary>
    /// <param name="grid"></param>
    /// <param name="index"></param>
    void SetGridsAlongAxis(int axis, DynamicGrid grid, int index)
    {
        //得到當前曲线的值
        float showCount = GetShowingCount();
        float fValue = 0.5f - (CurOffsetValue - index * InteralFactor) / InteralFactor / showCount;

        //取出当前各个曲线的值
        float posCurveValue = GetPositionValue(fValue, 0);
        float scaleCurveValue = GetScaleValue(fValue, 0);
        float depthCurveValue = DepthCurve.Evaluate(fValue);

        //设置位置和缩放
        grid.name = index.ToString();
        float size = ViewSize[axis];
        if (axis == 0)
            grid.rectTransform.anchoredPosition = new Vector2(posCurveValue * size - size / 2, AxisOffset);
        else
            grid.rectTransform.anchoredPosition = new Vector2(AxisOffset, posCurveValue * size - size / 2);

        grid.rectTransform.localScale = new Vector3(scaleCurveValue, scaleCurveValue, scaleCurveValue);

        //设置深度
        int newDepth = (int)(depthCurveValue * (float)transform.childCount);
        grid.rectTransform.SetSiblingIndex(newDepth);
    }


    /// <summary>
    ///  获取缩放曲线
    /// </summary>
    /// <param name="sliderValue"></param>
    /// <param name="added"></param>
    /// <returns></returns>
    private float GetScaleValue(float sliderValue, float added)
    {
        float scaleValue = ScaleCurve.Evaluate(sliderValue + added);
        return scaleValue;
    }
    /// <summary>
    ///  获取位置曲线
    /// </summary>
    /// <param name="sliderValue"></param>
    /// <param name="added"></param>
    /// <returns></returns>
    private float GetPositionValue(float sliderValue, float added)
    {
        float evaluateValue = PositionCurve.Evaluate(sliderValue + added);
        return evaluateValue;
    }


    /// <summary>
    /// 从本地加载一个Grid
    /// </summary>
    /// <returns></returns>
    protected virtual DynamicGrid LoadGridFormPool()
    {
        if (Grid == null)
        {
            Debug.LogError("Error:TableView m_Grid is null");
            return null;
        }

        DynamicGrid grid = DequeueCell();

        if (grid != null)
            return grid;


        GameObject obj = Instantiate(Grid.gameObject);
        grid = obj.GetComponent<DynamicGrid>();
        grid.transform.SetParent(Content, false);

        //内嵌套点击事件
        if (IsGridTouchEventEnable)
        {
            DynamicGridClickHelper trigger = grid.GetComponent<DynamicGridClickHelper>();
            if (trigger == null)
                trigger = grid.gameObject.AddComponent<DynamicGridClickHelper>();

            trigger.SetupClickEnable(true, delegate (PointerEventData eventData)
            {
                OnTableGridTouched(grid, eventData);
            });
        }
        else
        {
            DynamicGridClickHelper trigger = grid.GetComponent<DynamicGridClickHelper>();
            if (trigger != null)
                trigger.SetupClickEnable(false);
        }


        return grid;
    }

    /// <summary>
    /// 从池中获取Grid
    /// </summary>
    /// <returns></returns>
    protected DynamicGrid DequeueCell()
    {
        if (GridPoolStack.Count <= 0)
            return null;

        return GridPoolStack.Pop();
    }

    /// <summary>
    /// 回收节点
    /// </summary>
    /// <param name="grid"></param>
    private void RecycleTableGrid(DynamicGrid grid)
    {
        if (grid == null)
            return;

        OnTableGridRecycle(grid);
        grid.Index = -1;

        if (grid.gameObject.activeSelf)
            grid.gameObject.SetActive(false);

        GridPoolStack.Push(grid);
    }

    #region 事件
    /// <summary>
    /// 回收节点
    /// </summary>
    /// <param name="index"></param>
    public void OnTableGridRecycle(DynamicGrid grid)
    {
        if (DynamicTableGridDelegate == null)
            return;
        DynamicTableGridDelegate((int)LayoutRule.DYNAMIC_DELEGATE_EVENT.DYNAMIC_GRID_RECYCLE, grid);
    }

    /// <summary>
    /// Grid被点击
    /// </summary>
    /// <param name="index"></param>
    public void OnTableGridTouched(DynamicGrid grid, PointerEventData eventData = null)
    {
        if (DynamicTableGridDelegate == null)
            return;

        DynamicTableGridDelegate((int)LayoutRule.DYNAMIC_DELEGATE_EVENT.DYNAMIC_GRID_TOUCHED, grid);
    }

    /// <summary>
    /// 更新数据接口
    /// </summary>
    /// <param name="grid"></param>
    public void OnTableGridAtIndex(DynamicGrid grid)
    {
        if (DynamicTableGridDelegate == null)
            return;

        DynamicTableGridDelegate((int)LayoutRule.DYNAMIC_DELEGATE_EVENT.DYNAMIC_GRID_ATINDEX, grid);
    }

    /// <summary>
    /// 修正后回调
    /// </summary>
    public void OnTweenOver()
    {
        if (DynamicTableGridDelegate != null)
            DynamicTableGridDelegate((int)LayoutRule.DYNAMIC_DELEGATE_EVENT.DYNAMIC_TWEEN_OVER, null);
    }

    #endregion



    /// <summary>
    /// 获取Grid
    /// </summary>
    /// <param name="index">下标</param>
    /// <returns></returns>
    public DynamicGrid GetGridByIndex(int index)
    {
        foreach (var grid in UsingGridSet)
        {
            if (grid.Index != index)
                continue;

            return grid;
        }
        return null;
    }

    /// <summary>
    /// 回收
    /// </summary>
    void RecycleGrids()
    {
        if (PreRecycleGridStack == null || PreRecycleGridStack.Count <= 0)
            return;

        while (PreRecycleGridStack.Count > 0)
        {
            var grid = PreRecycleGridStack.Pop();
            UsingGridSet.Remove(grid);

            OnTableGridRecycle(grid);
            RecycleTableGrid(grid);
        }
    }

    /// <summary>
    /// 计算当前可视区域可以显示几个
    /// </summary>
    int GetShowingCount()
    {
        return CentralIndex * 2 - 1;
    }

    public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera)
    {
        return true;

    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (!IsActive() || !IsInitCompeleted)
            return;


        IsDragging = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(Content, eventData.position, eventData.pressEventCamera, out LastCursorStartPosition);
        PreCursorPosition = LastCursorStartPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        if (!IsActive() || !IsInitCompeleted)
            return;

        //获取当前滑动光标的位置
        Vector2 localCursor;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Content, eventData.position, eventData.pressEventCamera, out localCursor))
            return;

        //算出delta，更新光标
        Vector2 delta = localCursor - LastCursorStartPosition;
        LastCursorStartPosition = localCursor;
        OnDragGridMove(delta);
    }

    /// <summary>
    /// 拖拽移动
    /// </summary>
    /// <param name="delta"></param>
    public void OnDragGridMove(Vector2 delta)
    {
        int axis = (int)Direction;
        float axisValue = delta[axis];

        if (Mathf.Abs(axisValue) <= 0.0f)
            return;

        if (CurOffsetValue < 0 && CurOffsetValue > TotalOffsetValue)
            return;

        float dt = (axisValue / ViewSize[axis]) * InteralFactor * GetShowingCount();

        CurOffsetValue -= dt;

        if (CurOffsetValue > TotalOffsetValue)
            CurOffsetValue = TotalOffsetValue;
        else if (CurOffsetValue <= 0)
            CurOffsetValue = 0;

        OnGridsInOut();
    }


    /// <summary>
    /// Grid的回收和出现逻辑
    /// </summary>
    protected virtual void OnGridsInOut()
    {
        //计算出开始索引和结束索引
        int offest = Mathf.RoundToInt(CurOffsetValue / InteralFactor);

        //获得最近的一个修正位置
        if (!IsTweening)
            FixOffsetValue = offest * InteralFactor;

        StartIndex = Mathf.Clamp(offest, 0, TotalCount - 1);
        int endIndex = (StartIndex + CentralIndex - 1);
        endIndex = endIndex > TotalCount - 1 ? TotalCount - 1 : endIndex;

        //回收
        foreach (var grid in UsingGridSet)
        {
            if (grid.Index > endIndex || grid.Index < StartIndex - CentralIndex + 1)
            {
                PreRecycleGridStack.Push(grid);
            }
        }

        RecycleGrids();
        //出现
        for (int i = StartIndex - CentralIndex + 1; i <= endIndex; i++)
        {
            DynamicGrid grid = GetGridByIndex(i);

            if (grid == null)
                grid = TableGridAtIndex(i);
            else
                SetGridsAlongAxis((int)Direction, grid, i);
        }
    }


    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        IsDragging = false;
    }

    /// <summary>
    /// 处理
    /// </summary>
    private void LateUpdate()
    {
        if (!IsInitCompeleted)
            return;

        float deltaTime = Time.unscaledDeltaTime;

        //已经脱手，需要考虑惯性的问题
        if (!IsDragging && (Velocity != Vector2.zero))
        {
            Vector2 position = Vector2.zero;
            for (int axis = 0; axis < 2; axis++)
            {
                // 惯性运动
                if (Inertia)
                {
                    //速度根据摩擦系数递减
                    Velocity[axis] *= Mathf.Pow(DecelerationRate, deltaTime);
                    if (Mathf.Abs(Velocity[axis]) < 1)
                        Velocity[axis] = 0;
                    position[axis] += Velocity[axis] * deltaTime;
                }
                // 如果不使用惯性.
                else
                {
                    Velocity[axis] = 0;
                }

                OnDragGridMove(position);
            }
        }
        //如果是用惯性和滑动中，计算脱手前的速度
        if (IsDragging)
        {
            Vector3 newVelocity = (LastCursorStartPosition - PreCursorPosition) / deltaTime;
            Velocity = Vector3.Lerp(Velocity, newVelocity, deltaTime * 10);
            PreCursorPosition = LastCursorStartPosition;
        }

        //需要修正位置
        if (!IsDragging && Velocity == Vector2.zero && IsNeedTweenToFix)
        {
            if (!IsTweening && CurOffsetValue != FixOffsetValue)
            {
                IsTweening = true;
                CurrentDuration = 0.0f;
                TweenStartOffsetValue = CurOffsetValue;
            }

            if (IsTweening)
                TweenToFix(FixOffsetValue);
        }

    }

    /// <summary>
    /// 修正
    /// </summary>
    public void TweenToFix(float fixValue)
    {
        CurrentDuration += Time.deltaTime;

        if (CurrentDuration > LerpDuration)
            CurrentDuration = LerpDuration;

        float percent = CurrentDuration / LerpDuration;
        CurOffsetValue = Mathf.Lerp(TweenStartOffsetValue, fixValue, percent);

        OnGridsInOut();

        if (CurOffsetValue == FixOffsetValue || CurrentDuration >= LerpDuration)
        {
            TweenStartOffsetValue = 0;
            CurrentDuration = 0.0f;
            IsTweening = false;
            OnTweenOver();
        }
    }


    /// <summary>
    /// 跳转到某个索引
    /// </summary>
    /// <param name="index"></param>
    public void TweenToIndex(int index)
    {
        if (index == StartIndex)
            return;

        if (index < 0 || index >= TotalCount)
            return;

        FixOffsetValue = GetFixOffsetValueByIndex(index);
    }

    /// <summary>
    /// 获得修正偏移
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public float GetFixOffsetValueByIndex(int index)
    {
        if (index < 0 || index >= TotalCount)
            return 0.0f;

        return index * InteralFactor;
    }

    /// <summary>
    /// 清理
    /// </summary>
    public void Clear()
    {
        foreach (var grid in UsingGridSet)
        {
            if (grid != null)
            {
                if (!Application.isPlaying)
                    DestroyImmediate(grid.gameObject);
                else
                    Destroy(grid.gameObject);
            }
        }

        while (GridPoolStack.Count > 0)
        {
            var grid = GridPoolStack.Pop();
            if (grid != null)
            {
                if (!Application.isPlaying)
                    DestroyImmediate(grid.gameObject);
                else
                    Destroy(grid.gameObject);
            }
        }

        UsingGridSet.Clear();
        GridPoolStack.Clear();

        IsInitCompeleted = false;
    }
}