﻿using AutomaticGroupSemipureStock.Datas;
using AutomaticGroupSemipureStock.Manager;
using AutomaticGroupSemipureStock.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace AutomaticGroupSemipureStock.Pages
{
    /// <summary>
    /// 自动组半纯品库页面
    /// </summary>
    public partial class PageAutomaticSemipureStock : Page, PageAction
    {
        // ---------------数据们-----------------
        private ObservableCollection<AutomaticStockInfo> _MainList;     // 主列表
        private ObservableCollection<AutomaticStockInfo> _CurrentList;  // 当前列表

        // ----------------筛选-------------------
        private NnCheckView CurrentCheckView;// 当前筛选视图
        private HashSet<NnCheckView> CheckViews;
        public PageAutomaticSemipureStock()
        {
            InitializeComponent();
            CheckViews = new HashSet<NnCheckView>();
            Init();
        }

        private async void Init()
        {
            MainWindow.StatusBar(true, "加载数据...");
            try
            {
#if (DEBUG)
                _MainList = await WEBHelper.HttpGetJSONAsync<ObservableCollection<AutomaticStockInfo>>("http://10.11.30.155:5000/api/stockinfo/AutomaticStockInfos");
#else
                _MainList = await WEBHelper.HttpGetJSONAsync<ObservableCollection<AutomaticStockInfo>>("http://10.11.30.155:5004/api/stockinfo/AutomaticStockInfos");
#endif
                foreach (var v in _MainList)
                {
                    v.Update = UpdateAndSave;
                }
                _CurrentList = _MainList;
                mDGMain.ItemsSource = _CurrentList;
            }
            catch (Exception e)
            {
                MainWindow.StatusBar(false, $"数据加载出现错误：{e.Message}");
                return;
            }
            MainWindow.StatusBar();
        }

        /// <summary>
        /// 保存与更新
        /// </summary>
        /// <param name="obj"></param>
        private async void UpdateAndSave(AutomaticStockInfo obj)
        {
            try
            {
#if (DEBUG)
                var v = await WEBHelper.HttpPostBodyAsync("http://10.11.30.155:5000/api/stockinfo/AutomaticStockUpdate", obj);
#else
                var v = await WEBHelper.HttpPostBodyAsync("http://10.11.30.155:5004/api/stockinfo/AutomaticStockUpdate", obj);
#endif
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                MainWindow.StatusBar(false, $"数据更新出现错误：{e.Message},ID：{obj.ID}");
            }
        }

        /// <summary>
        /// 移除库存
        /// </summary>
        private async void RemoveStock()
        {
            HashSet<AutomaticStockInfo> ll = GetSelectedItems();
            if (ll.Count <= 0) return;
            Dictionary<long, AutomaticStockInfo> pairs = new Dictionary<long, AutomaticStockInfo>();
            HashSet<long> ls = new HashSet<long>();
            foreach (var v in ll)
            {
                ls.Add(v.ID);
                pairs.Add(v.ID, v);
            }
            try
            {
#if(DEBUG)
                List<long> vs = await WEBHelper.HttpPostJSONAsync<List<long>>("http://10.11.30.155:5000/api/stockinfo/AutomaticStockRemove", ls);
#else
                List<long> vs = await WEBHelper.HttpPostJSONAsync<List<long>>("http://10.11.30.155:5004/api/stockinfo/AutomaticStockRemove", ls);
#endif
                foreach (var v in vs)
                {
                    if (pairs.ContainsKey(v))
                    {
                        _CurrentList.Remove(pairs[v]);
                        _MainList.Remove(pairs[v]);
                    }
                }
            }
            catch (Exception e)
            {
                MainWindow.StatusBar(false, $"移除库存出现错误：{e.Message}");
            }
        }

        private HashSet<AutomaticStockInfo> GetSelectedItems()
        {
            HashSet<AutomaticStockInfo> ll = new HashSet<AutomaticStockInfo>();
            foreach(var v in mDGMain.SelectedCells)
            {
                var asi = v.Item as AutomaticStockInfo;
                if (asi == null) continue;
                ll.Add(asi);
            }
            return ll;
        }

#region 事件
        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            switch (((Control)sender).Tag)
            {
                case "edit":        // 开始编辑
                    MainWindow.Action?.Invoke("edit");
                    break;
                case "opentable":   // 打开表格
                    OpenTable();
                    break;
                case "removestock": // 移除库存
                    RemoveStock();
                    break;
                default:return;
            }
        }

        private void mSBMain_OnSearching(object sender, Views.SearcherRoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.SearchValue)) return;
            MainWindow.Search(e.SearchValue);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            switch (((Control)sender).Tag)
            {
                case "refresh":         // 刷新
                    _removeFilter();
                    Init();
                    break;
                case "removefilter":    // 移除所有筛选
                    _removeFilter();
                    break;
                default: return;
            }
        }
#endregion // 事件

#region 筛选
        /// <summary>
        /// 清除所有筛选
        /// </summary>
        private void _removeFilter()
        {
            _CurrentList = _MainList;
            mDGMain.ItemsSource = _CurrentList;

            foreach (var v in CheckViews)
            {
                if (v.IsChecked) v.RemoveCheck();
            }
        }
        private void NnCheckView_OnCheck(object sender, RoutedEventArgs e)
        {
            var view = sender as NnCheckView;
            if (view == null) return;

            if (!CheckViews.Contains(view))
            {
                CheckViews.Add(view);
            }

            view.CurrentList = _CurrentList;
            if (CurrentCheckView != view)
            {
                view.ReLoadData();
            }
            CurrentCheckView = view;
        }

        private void NnCheckView_StartFilter(object sender, RoutedEventArgs e)
        {
            var nck = sender as NnCheckView;
            if (nck == null) return;
            switch (nck.CheckAction)
            {
                case "screeningok":// 筛选确定
                    break;
                case "clear":// 取消筛选
                    CurrentCheckView = null;
                    break;
                default: return;
            }
            _sereeningOKNew();
        }

        private void _sereeningOKNew()
        {
            var pairs = new Dictionary<string, NnCheckView>();
            foreach (var v in CheckViews)
            {
                if (v.IsChecked && !pairs.ContainsKey(v.Trait))
                {
                    pairs.Add(v.Trait, v);
                }
            }
            if (pairs.Count <= 0)
            {
                _CurrentList = _MainList;
                mDGMain.ItemsSource = _CurrentList;
                return;
            }
            _CurrentList = new ObservableCollection<AutomaticStockInfo>();
            foreach (var v in _MainList)
            {
                if (IsFilterPassed(v, pairs))
                {
                    _CurrentList.Add(v);
                }
            }
            mDGMain.ItemsSource = _CurrentList;
        }

        /// <summary>
        /// 筛选是否通过
        /// </summary>
        private bool IsFilterPassed(object op, Dictionary<string, NnCheckView> pairs)
        {
            foreach (var v in pairs)
            {
                if (!v.Value.CheckPassed(op))
                {
                    return false;
                }
            }
            return true;
        }
#endregion // 筛选


        /// <summary>
        /// 打开表格
        /// </summary>
        public async void OpenTable()
        {
            if (_CurrentList == null || _CurrentList.Count <= 0) return;

            MainWindow.StatusBar?.Invoke(true, "打开表格...");

            try
            {
                await Task.Run(() =>
                {
                    ExcelManager.OpenTable(_CurrentList);
                });
            }
            catch { MainWindow.StatusBar?.Invoke(false, "打开表格出现错误，请重试!"); return; }

            MainWindow.StatusBar?.Invoke();
        }
    }
}
