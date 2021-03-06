﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace StockManagementSystem.data
{
    /// <summary>
    /// 用于对数据库的操作以及维护等工作
    /// </summary>
    public class NnStockManager
    {
        private OleDbConnection m_connection;// 数据库
        private StreamWriter m_writer;// 用于写入数据到文件
        string m_path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\nnns\";

        private Random mRandom = new Random();

        //弹出通知窗口（通知窗口有两种，一种自动消失（表示提示,isWarning为false），一种需要手动关闭（表示信息重要，必须引起客户重视，isWarning为true））
        public delegate void NnShowMessage(string message, bool isWarning);

        public NnShowMessage ShowMessage { get; set; }

        /// <summary>
        /// NnStockManager是否初始化正常并且可用
        /// </summary>
        public bool IsValid { get; set; }

        public NnStockManager(NnShowMessage nnShow)
        {
            ShowMessage += nnShow;
            init();
        }

        /// <summary>
        /// 数据库文件所在路径
        /// </summary>
        public string DatabasePath { get; set; }

        // 验证密码
        public bool IsPassed(string name, string md5)
        {
            using (OleDbCommand cmd = new OleDbCommand("select * from [_users] where [_user]=@na", m_connection))
            {
                cmd.Parameters.AddWithValue("na", name);
                try
                {
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string str = reader.GetString(reader.GetOrdinal("password"));
                            return md5 == (reader.GetString(reader.GetOrdinal("password")));
                        }
                    }
                }
                catch { }
            }
            return false;
        }

        // 添加用户
        public int AddUser(string name, string md5)
        {
            int count = 0;
            using (OleDbCommand cmd = new OleDbCommand("insert into [_users] ([_user],[password]) values(@na,@md)", m_connection))
            {
                cmd.Parameters.AddWithValue("na", name);
                cmd.Parameters.AddWithValue("md", md5);
                try
                {
                    count = cmd.ExecuteNonQuery();
                }
                catch { }
            }
            return count;
        }

        /// <summary>
        /// 导出未QC坐标
        /// </summary>
        internal void OutputNotQCCoordinate()
        {
            List<Coordinates> list = new List<Coordinates>();
            using (OleDbCommand cmd = new OleDbCommand("select * from notqccoordinate", m_connection))
            {
                try
                {
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string box = reader["box"] as string;
                            string coos = reader["coo"] as string;
                            if (string.IsNullOrWhiteSpace(coos)) continue;
                            string[] cos = coos.Split(',');
                            try
                            {
                                foreach (var v in cos)
                                {
                                    if (string.IsNullOrWhiteSpace(v)) continue;
                                    Coordinates coo = new Coordinates();
                                    coo.Plate = box;
                                    coo.Coordinate = v;
                                    list.Add(coo);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            StringBuilder sb = new StringBuilder();
            list.Sort((x, y) =>
            {
                int i = x.Plate.CompareTo(y.Plate);
                if (i != 0)
                {
                    return i;
                }
                return (int)(GetMaxValue(x.Coordinate) - GetMaxValue(y.Coordinate));
            });
            sb.Append("坐标").Append('\n');
            foreach (var v in list)
            {
                sb.Append(v.Plate).Append('-').Append(v.Coordinate).Append('\n');
            }
            _writeToFileAndOpen(sb.ToString());
        }

        struct Coordinates
        {
            public string Plate;
            public string Coordinate;
        }

        /// <summary>
        /// 导出所有的可用坐标
        /// </summary>
        /// <returns></returns>
        public void OutputCoordinate()
        {
            List<Coordinates> list = new List<Coordinates>();
            using (OleDbCommand cmd = new OleDbCommand("select * from coordinate", m_connection))
            {
                try
                {
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            try
                            {
                                string plate = reader["plate"] as string;
                                string coos = reader["coo"] as string;
                                if (string.IsNullOrWhiteSpace(coos)) continue;
                                string[] cos = coos.Split(',');
                                foreach (var v in cos)
                                {
                                    if (string.IsNullOrWhiteSpace(v)) continue;
                                    Coordinates co = new Coordinates();
                                    co.Plate = plate;
                                    co.Coordinate = v;
                                    list.Add(co);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            list.Sort((x, y) =>
            {
                int i = x.Plate.CompareTo(y.Plate);
                if (i != 0)
                {
                    return i;
                }
                return (int)(GetMaxValue(x.Coordinate) - GetMaxValue(y.Coordinate));
            });
            StringBuilder sb = new StringBuilder();
            foreach (var v in list)
            {
                sb.Append(v.Plate).Append('-').Append(v.Coordinate).Append('\n');
            }
            _writeToFileAndOpen(sb.ToString());
        }

        // 用于写入文件并且打开文件
        private void _writeToFileAndOpen(string str)
        {
            try
            {
                string path = m_path + DateTime.Now.Ticks + ".csv";
                DirectoryInfo dir = new DirectoryInfo(m_path);
                if (!dir.Exists) dir.Create();
                m_writer = new StreamWriter(path, false, Encoding.UTF8);

                m_writer.Write(str);
                m_writer.Flush();
                m_writer.Close();
                System.Diagnostics.Process.Start(path);

            }
            catch { ShowMessage?.Invoke("文件写入失败", true); }
        }

        /// <summary>
        /// 搜索所有
        /// </summary>
        internal string SearchAll(string value)
        {
            List<SearchItem> list = SearchItem.GetSearchItems(value);
            NnExcel ne = new NnExcel();
            ne.Visible = true;
            // 在库存中搜索
            _searchStock(list, ne);
            // 在未QC库中搜索
            _searchNotQC(list, ne);

            // 在未树脂肽库中搜索
            _searchResin(list, ne);

            ne.Workbook.Worksheets[1].Activate();
            return "";
        }

        /// <summary>
        /// 在库存中搜索
        /// </summary>
        private void _searchStock(List<SearchItem> list, NnExcel ne)
        {
            StockSearcher stSearcher = new StockSearcher();
            foreach (var v in list)
            {
                _getSearchValues(v.Value, stSearcher);
            }
            object[][] vs = stSearcher.GetValues();


            ne.AddValueWithName(vs, 1, 11, "库存");
            ne.AutoFit(new int[] { 2, 3, 4 });
            ne.SetColor(NPoint.GetPoint(1, 1), NPoint.GetPoint(1, 9), NnExcel.ColorType.Blue);
            ne.SetColor(NPoint.GetPoint(stSearcher.CountNormal + 2, 1), NPoint.GetPoint(stSearcher.CountNormal + 3, 11), NnExcel.ColorType.Blue);

        }

        /// <summary>
        /// 在树脂肽中搜索
        /// </summary>
        /// <param name="list"></param>
        /// <param name="ne"></param>
        private void _searchResin(List<SearchItem> list, NnExcel ne)
        {
            StockInfos infos = new StockInfos();
            try
            {
                using (OleDbCommand cmd = new OleDbCommand())
                {
                    cmd.Connection = m_connection;
                    foreach (var v in list)
                    {
                        if (string.IsNullOrWhiteSpace(v.Value)) continue;
                        if (v.SearchType == SearchType.WorkNo)
                        {
                            _initResinWorkNoSearch(infos, cmd, v);
                        }
                        else
                        {
                            _initResinCoordinateAndOrderIDSearch(infos, cmd, v);
                        }
                    }
                }
                object[][] vs = infos.GetValues();

                ne.AddValueWithName(vs, 1, 9, "树脂肽");
                ne.AutoFit(new int[] { 2, 3, 4, 8 });
                ne.SetColor(NPoint.GetPoint(1, 1), NPoint.GetPoint(1, 7), NnExcel.ColorType.Blue);
                ne.SetColor(NPoint.GetPoint(infos.CountNormal + 2, 1), NPoint.GetPoint(infos.CountNormal + 3, 9), NnExcel.ColorType.Blue);
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
        }

        /// <summary>
        /// 在未QC库中搜索
        /// </summary>
        private void _searchNotQC(List<SearchItem> list, NnExcel ne)
        {
            StockInfos infos = new StockInfos();
            try
            {
                using (OleDbCommand cmd = new OleDbCommand())
                {
                    cmd.Connection = m_connection;
                    foreach (var v in list)
                    {
                        if (string.IsNullOrWhiteSpace(v.Value)) continue;
                        if (v.SearchType == SearchType.WorkNo)
                        {
                            _initNotQCWorkNoSearch(infos, cmd, v);
                        }
                        else
                        {
                            _initNotQCCoordinateAndOrderIDSearch(infos, cmd, v);
                        }
                    }
                }
                object[][] vs = infos.GetValues();

                ne.AddValueWithName(vs, 1, 9, "半纯品");
                ne.AutoFit(new int[] { 2, 3, 4, 8 });
                ne.SetColor(NPoint.GetPoint(1, 1), NPoint.GetPoint(1, 7), NnExcel.ColorType.Blue);
                ne.SetColor(NPoint.GetPoint(infos.CountNormal + 2, 1), NPoint.GetPoint(infos.CountNormal + 3, 9), NnExcel.ColorType.Blue);
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
        }

        private void _initNotQCCoordinateAndOrderIDSearch(StockInfos infos, OleDbCommand cmd, SearchItem v)
        {
            cmd.Parameters.Clear();

            cmd.CommandText = "SELECT * FROM notqc WHERE Coordinate=@cd";
            cmd.Parameters.AddWithValue("cd", v.Value);

            bool b = _initNotQCOrderInfo(infos, cmd, v);

            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT * FROM notqc WHERE OrderId=@id";
            cmd.Parameters.AddWithValue("id", v.Value);

            _initNotQCOrderInfo(infos, cmd, v, !b);
        }
        private void _initResinCoordinateAndOrderIDSearch(StockInfos infos, OleDbCommand cmd, SearchItem v)
        {
            cmd.Parameters.Clear();

            cmd.CommandText = "SELECT * FROM resinpeptide WHERE Coordinate=@cd";
            cmd.Parameters.AddWithValue("cd", v.Value);

            bool b = _initNotQCOrderInfo(infos, cmd, v);

            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT * FROM resinpeptide WHERE OrderId=@id";
            cmd.Parameters.AddWithValue("id", v.Value);

            _initNotQCOrderInfo(infos, cmd, v, !b);
        }

        private bool _initNotQCOrderInfo(StockInfos infos, OleDbCommand cmd, SearchItem v, bool isfirst = false)
        {
            bool isFirst = true;
            using (OleDbDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    OrderInfo od = new OrderInfo();
                    od.InitNotQCOrderByDB(reader);
                    od.OriginalString = v.Value;
                    od.SearchState = od.State == OrderInfo.OrderInfoState.Insert ? SearchState.Normal : SearchState.Deleted;
                    infos.Add(od);
                    if (od.State == OrderInfo.OrderInfoState.Insert)
                        isFirst = false;
                }
                if (isFirst && !isfirst)
                {
                    OrderInfo od = new OrderInfo() { SearchState = SearchState.None, OriginalString = v.Value };
                    infos.Add(od);
                }
            }
            return !isFirst;
        }


        private void _initResinWorkNoSearch(StockInfos infos, OleDbCommand cmd, SearchItem v)
        {
            cmd.Parameters.Clear();

            cmd.CommandText = "SELECT * FROM resinpeptide WHERE WorkNo=@wn";
            cmd.Parameters.AddWithValue("wn", v.Value);

            _initNotQCOrderInfo(infos, cmd, v);
        }

        private void _initNotQCWorkNoSearch(StockInfos infos, OleDbCommand cmd, SearchItem v)
        {
            cmd.Parameters.Clear();

            cmd.CommandText = "SELECT * FROM notqc WHERE WorkNo=@wn";
            cmd.Parameters.AddWithValue("wn", v.Value);

            _initNotQCOrderInfo(infos, cmd, v);
        }


        private void _getSearchValues(string s, StockSearcher ss)
        {
            bool isHas;
            if (!s.Contains('-'))// 如果是workNo
            {
                isHas = _searchByWorkNo(s, ss);
            }
            else// 否则
            {
                isHas = _searchByOrderIdAndCoordinate(s, ss);
            }
            if (!isHas)
            {
                NnStock ns = new NnStock() { StockSearchState = SearchState.None, OriginalString = s };
                ss.Add(ns);
            }
        }

        /// <summary>
        /// 通过OrderId和Coordinate搜索
        /// </summary>
        private bool _searchByOrderIdAndCoordinate(string s, StockSearcher ss)
        {
            bool isHas = false;
            try
            {
                using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM stock_new WHERE [coordinate]=@v1", m_connection))
                {
                    cmd.Parameters.AddWithValue("v1", s);
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            NnStock ns = new NnStock();
                            ns.InitStockNewByDb(reader);
                            ns.OriginalString = s;
                            ss.Add(ns);
                            return true;
                        }
                    }
                    cmd.CommandText = "SELECT * FROM stock_new WHERE [orderId]=@v1";
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            NnStock ns = new NnStock();
                            ns.InitStockNewByDb(reader);
                            ns.OriginalString = s;
                            isHas = true;
                            ss.Add(ns);
                        }
                    }
                    cmd.CommandText = "SELECT * FROM stock_old WHERE [orderId]=@v1";
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            NnStock ns = new NnStock();
                            ns.InitStockOldByDb(reader);
                            ns.OriginalString = s;
                            ss.Add(ns);
                        }
                    }
                }
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
            return isHas;
        }

        /// <summary>
        /// 通过workno搜索
        /// </summary>
        private bool _searchByWorkNo(string s, StockSearcher ss)
        {
            bool isHas = false;
            try
            {
                using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM stock_new WHERE [workNo]=@v1", m_connection))
                {
                    cmd.Parameters.AddWithValue("v1", s);
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            NnStock ns = new NnStock();
                            ns.InitStockNewByDb(reader);
                            ns.OriginalString = s;
                            ss.Add(ns);
                            isHas = true;
                        }
                    }
                    cmd.CommandText = "SELECT * FROM stock_old WHERE [workNo]=@v1";
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            NnStock ns = new NnStock();
                            ns.InitStockOldByDb(reader);
                            ns.OriginalString = s;
                            ss.Add(ns);
                        }
                    }
                }
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
            return isHas;
        }

        // 删除自己创建的无用文件
        private void _deleteOtherFile()
        {
            try
            {
                DirectoryInfo info = new DirectoryInfo(m_path);
                foreach (FileInfo finfo in info.GetFiles())
                {
                    try
                    {
                        finfo.Delete();
                    }
                    catch { }
                }
            }
            catch { }
        }


        /// <summary>
        /// 提交未QC订单
        /// </summary>
        /// <param name="stock"></param>
        /// <returns></returns>
        internal int SubmitNotQCOrder(OrderInfo order)
        {
            switch (order.State)
            {
                case OrderInfo.OrderInfoState.Insert:
                    return _addNotQCIntoDB(order);
                case OrderInfo.OrderInfoState.Remove:
                    return _removeNotQCFromDB(order);
                default: return 0;
            }
        }

        private int _removeNotQCFromDB(OrderInfo order)
        {
            int count = 0;
            try
            {
                using (OleDbCommand cmd = new OleDbCommand("UPDATE notqc SET DateRemove=@dr,Coordinate=notqc.ID,Comments=@cs,Cause=@cu WHERE Coordinate=@cd", m_connection))
                {
                    cmd.Parameters.AddWithValue("dr", DateTime.Now.ToString());
                    cmd.Parameters.AddWithValue("cs", order.Comments);
                    cmd.Parameters.AddWithValue("cu", order.Cause);
                    cmd.Parameters.AddWithValue("cd", order.Coordinate);
                    count = cmd.ExecuteNonQuery();
                }
                if (count > 0)
                    _updateNotQCCoordinate(order.Coordinate, false);
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
            return count;
        }

        private int _addNotQCIntoDB(OrderInfo order)
        {
            int count = 0;
            try
            {
                using (OleDbCommand cmd = new OleDbCommand("INSERT INTO notqc (DateAdd,WorkNo,OrderId,Quality,Coordinate,Comments) VALUES(@da,@wn,@oi,@qt,@cd,@cm)", m_connection))
                {
                    cmd.Parameters.AddWithValue("da", DateTime.Now.ToString());
                    cmd.Parameters.AddWithValue("wn", order.WorkNo);
                    cmd.Parameters.AddWithValue("oi", order.OrderId);
                    cmd.Parameters.AddWithValue("qt", order.Quality);
                    cmd.Parameters.AddWithValue("cd", order.Coordinate);
                    cmd.Parameters.AddWithValue("cm", order.Comments);
                    count = cmd.ExecuteNonQuery();
                }
                if (count > 0)
                    _updateNotQCCoordinate(order.Coordinate, true);
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
            return count;
        }

        /// <summary>
        /// 提交树脂肽数据
        /// </summary>
        internal int SubmitResinOrder(OrderInfo order)
        {
            switch (order.State)
            {
                case OrderInfo.OrderInfoState.Insert:// 添加
                    return _addResinIntoDB(order);
                case OrderInfo.OrderInfoState.Remove:// 移除
                    return _removeResinFromDB(order);
                default: return 0;
            }
        }

        /// <summary>
        /// 添加树脂肽
        /// </summary>
        private int _addResinIntoDB(OrderInfo order)
        {
            int count = 0;
            try
            {
                using (OleDbCommand cmd = new OleDbCommand("INSERT INTO resinpeptide (DateAdd,WorkNo,OrderId,Quality,Coordinate,Comments) VALUES(@da,@wn,@oi,@qt,@cd,@cm)", m_connection))
                {
                    cmd.Parameters.AddWithValue("da", DateTime.Now.ToString());
                    cmd.Parameters.AddWithValue("wn", order.WorkNo);
                    cmd.Parameters.AddWithValue("oi", order.OrderId);
                    cmd.Parameters.AddWithValue("qt", order.Quality);
                    cmd.Parameters.AddWithValue("cd", order.Coordinate);
                    cmd.Parameters.AddWithValue("cm", order.Comments);
                    count = cmd.ExecuteNonQuery();
                }
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
            return count;
        }
        /// <summary>
        /// 移除树脂肽
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        private int _removeResinFromDB(OrderInfo order)
        {
            int count = 0;
            try
            {
                using (OleDbCommand cmd = new OleDbCommand("UPDATE resinpeptide SET DateRemove=@dr,Coordinate=resinpeptide.ID,Comments=@cs,Cause=@cu WHERE Coordinate=@cd", m_connection))
                {
                    cmd.Parameters.AddWithValue("dr", DateTime.Now.ToString());
                    cmd.Parameters.AddWithValue("cs", order.Comments);
                    cmd.Parameters.AddWithValue("cu", order.Cause);
                    cmd.Parameters.AddWithValue("cd", order.Coordinate);
                    count = cmd.ExecuteNonQuery();
                }
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
            return count;
        }

        internal void AllNotQCData()
        {
            StringBuilder sbnew = new StringBuilder();
            sbnew.Append("日期,WO号,Order ID,质量,坐标,备注\n");

            StringBuilder sbold = new StringBuilder();
            sbold.Append("日期,WO号,Order ID,质量,坐标,备注,取走日期,原因\n");
            try
            {
                using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM notqc ORDER BY Coordinate DESC", m_connection))
                {
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            OrderInfo od = new OrderInfo();
                            od.InitNotQCOrderByDB(reader);
                            if (od.State == OrderInfo.OrderInfoState.Insert)
                            {
                                sbnew.Append(od.ToString());
                            }
                            else
                            {
                                sbold.Append(od.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
            _writeToFileAndOpen(sbnew.ToString() + "\n\n已被移除项目：\n" + sbold.ToString());
        }

        /// <summary>
        /// 将更改提交到数据库
        /// </summary>
        /// <param name="stock"></param>
        /// <returns></returns>
        public int Submit(NnStock stock)
        {
            switch (stock.State)
            {
                case NnStock.StockState.Insert:// 添加
                    return _addIntoDatabase(stock);
                case NnStock.StockState.Update:// 更新
                    return _updateForDatabase(stock);
                case NnStock.StockState.Delete:// 移除
                    return _removeFromDatabase(stock);
                default: return 0;
            }
        }

        // 更新数据库数据
        private int _updateForDatabase(NnStock stock)
        {
            int count = 0;
            using (OleDbCommand cmd = new OleDbCommand("select * from stock_new where coordinate=@cd", m_connection))
            {
                cmd.Parameters.AddWithValue("cd", stock.Coordinate);
                try
                {
                    NnStock sk = null;
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            sk = new NnStock();
                            sk.InitStockNewByDb(reader);
                            sk.Purity = stock.Purity;
                            sk.Mw = stock.Mw;
                            if (stock.Quality > 0)
                                sk.Quality = stock.Quality;
                        }
                    }
                    if (sk == null) return count;
                    cmd.Parameters.Clear();
                    cmd.CommandText = "update stock_new set quality=@qt,purity=@pt,mw=@mw where coordinate=@cd";
                    cmd.Parameters.AddWithValue("qt", sk.Quality);
                    cmd.Parameters.AddWithValue("pt", sk.Purity);
                    cmd.Parameters.AddWithValue("mw", sk.Mw);
                    cmd.Parameters.AddWithValue("cd", sk.Coordinate);
                    count = cmd.ExecuteNonQuery();
                }
                catch (Exception e) { Console.WriteLine(e.ToString()); }
            }
            return count;
        }

        // TODO自增ID 加入数据库
        private int _addIntoDatabase(NnStock stock)
        {
            _initStockCoordinate(stock);// 检测是否是临时坐标，并确保临时坐标不会重复
            int count = 0;
            using (OleDbCommand cmd = new OleDbCommand("", m_connection))
            {
                cmd.CommandText = "insert into stock_new([_date], workNo, orderId, quality, coordinate, purity, mw,comments) " +
                $"values(@de,@wn,@oi,@qt,@cd,@pt,@mw,@cs)";
                cmd.Parameters.AddWithValue("de", DateTime.Now.ToString());
                cmd.Parameters.AddWithValue("wn", stock.WorkNo);
                cmd.Parameters.AddWithValue("oi", stock.OrderId);
                cmd.Parameters.AddWithValue("qt", stock.Quality);
                cmd.Parameters.AddWithValue("cd", stock.Coordinate);
                cmd.Parameters.AddWithValue("pt", stock.Purity);
                cmd.Parameters.AddWithValue("mw", stock.Mw);
                cmd.Parameters.AddWithValue("cs", stock.Comments);
                try
                {
                    count = cmd.ExecuteNonQuery();
                }
                catch (Exception e) { Console.WriteLine(e.ToString()); }

                if (count > 0)
                {
                    _updateCoordinate(stock.Coordinate, true);
                    // 移除临时库存
                    _deleteTemporaryStock(stock);
                }
            }
            return count;
        }
        /// <summary>
        /// 检测是否是临时坐标，并确保临时坐标不会重复
        /// </summary>
        private void _initStockCoordinate(NnStock stock)
        {
            if (!stock.Coordinate.StartsWith("L-")) return;
            int count = SelectStockCountByDate(DateTime.Now);
            stock.Coordinate += $"-{count + 1}{mRandom.Next(0, 100)}";
        }

        private int SelectStockCountByDate(DateTime now)
        {
            var time = now.AddHours((now.Hour + 1) * -1);
            int count = 0;
            try
            {
                using (OleDbCommand cmd = new OleDbCommand("select COUNT(*) from stock_new where [_date]>@dt", m_connection))
                {
                    cmd.Parameters.AddWithValue("dt", time.ToString());
                    count = Convert.ToInt32(cmd.ExecuteScalar());
                }
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
            return count;
        }

        /// <summary>
        /// 移除临时库存
        /// </summary>
        /// <param name="stock"></param>
        private void _deleteTemporaryStock(NnStock stock)
        {
            List<NnStock> list = new List<NnStock>();
            using (OleDbCommand cmd = new OleDbCommand("SELECT * FROM stock_new WHERE [orderId]=@v1", m_connection))
            {
                cmd.Parameters.AddWithValue("v1", stock.OrderId);
                using (OleDbDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        NnStock sk = new NnStock();
                        sk.InitStockNewByDb(reader);
                        if (sk.Coordinate.StartsWith("L-") && sk.Coordinate != stock.Coordinate)
                            list.Add(sk);
                    }
                }
            }
            foreach (var v in list)
            {
                v.Cause = "坐标替换" + v.Coordinate + "->" + stock.Coordinate;
                _removeFromDatabase(v);
            }
        }

        // 从数据库移除
        private int _removeFromDatabase(NnStock stock)
        {
            int count = 0;
            using (OleDbCommand cmd = new OleDbCommand("select * from stock_new where coordinate=@cd", m_connection))
            {
                cmd.Parameters.AddWithValue("cd", stock.Coordinate);
                try
                {
                    NnStock sk = null;
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            sk = new NnStock();
                            sk.InitStockNewByDb(reader);
                        }
                    }
                    if (sk == null) return count;

                    cmd.Parameters.Clear();
                    cmd.CommandText = $"insert into stock_old (dateAdd,dateRemove,workNo,orderId,quality,purity,mw,cause) values(@dta,@dtn,@wn,@oi,@qt,@pt,@mw,@ca)";
                    cmd.Parameters.AddWithValue("dta", sk.DateAdd.ToString());
                    cmd.Parameters.AddWithValue("dtn", DateTime.Now.ToString());
                    cmd.Parameters.AddWithValue("wn", sk.WorkNo);
                    cmd.Parameters.AddWithValue("oi", sk.OrderId);
                    cmd.Parameters.AddWithValue("qt", sk.Quality);
                    cmd.Parameters.AddWithValue("pt", sk.Purity);
                    cmd.Parameters.AddWithValue("mw", sk.Mw);
                    cmd.Parameters.AddWithValue("ca", stock.Cause);
                    count = cmd.ExecuteNonQuery();
                    if (count > 0)
                    {
                        cmd.Parameters.Clear();
                        cmd.CommandText = "delete from stock_new where coordinate=@cd";
                        cmd.Parameters.AddWithValue("cd", stock.Coordinate);
                        cmd.ExecuteNonQuery();
                        _updateCoordinate(stock.Coordinate, false);
                    }
                }
                catch (Exception e) { Console.WriteLine(e.ToString()); }
            }
            return count;
        }

        private void _updateNotQCCoordinate(string coordinate, bool isRemove)
        {
            try
            {
                int index = coordinate.IndexOf('-');
                if (index < 0) return;
                string box = coordinate.Substring(0, index);
                string place = coordinate.Substring(index + 1, coordinate.Length - index - 1);
                using (OleDbCommand cmd = new OleDbCommand("select * from notqccoordinate where box = @bx", m_connection))
                {
                    cmd.Parameters.AddWithValue("bx", box);
                    string newplace = null;
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            if (isRemove)
                                newplace = (reader["coo"] as string).Replace(place + ",", "");
                            else
                                newplace = ((reader["coo"] as string) ?? "") + place + ",";
                        }
                    }
                    if (newplace != null)
                    {
                        cmd.Parameters.Clear();
                        cmd.CommandText = "update notqccoordinate set coo = @np where [box] = @pt";
                        cmd.Parameters.AddWithValue("np", newplace);
                        cmd.Parameters.AddWithValue("pt", box);
                        cmd.ExecuteNonQuery();
                    }
                    else if (!isRemove)
                    {
                        cmd.Parameters.Clear();
                        cmd.CommandText = "insert into notqccoordinate ([box],coo) values(@box,@ppt)";
                        cmd.Parameters.AddWithValue("box", box);
                        cmd.Parameters.AddWithValue("ppt", place + ",");
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
        }

        /// <summary>
        /// 更新数据库坐标
        /// </summary>
        /// <param name="coordinate">坐标</param>
        /// <param name="isRemove">是否移除坐标</param>
        private void _updateCoordinate(string coordinate, bool isRemove)
        {
            try
            {
                int index = coordinate.IndexOf('-');
                if (index < 0) return;
                string plate = coordinate.Substring(0, index);
                string place = coordinate.Substring(index + 1, coordinate.Length - index - 1);

                if (plate == "L") return;

                using (OleDbCommand cmd = new OleDbCommand("select * from coordinate where plate = @pt", m_connection))
                {
                    cmd.Parameters.AddWithValue("pt", plate);
                    string newplace = null;
                    using (OleDbDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            if (isRemove)
                                newplace = (reader["coo"] as string).Replace(place + ",", "");
                            else
                                newplace = ((reader["coo"] as string) ?? "") + place + ",";
                        }
                    }
                    if (newplace != null)
                    {
                        cmd.Parameters.Clear();
                        cmd.CommandText = "update coordinate set coo = @np where plate = @pt";
                        cmd.Parameters.AddWithValue("np", newplace);
                        cmd.Parameters.AddWithValue("pt", plate);
                        cmd.ExecuteNonQuery();
                    }
                    else if (!isRemove)
                    {
                        cmd.Parameters.Clear();
                        cmd.CommandText = "insert into coordinate (plate,coo) values(@pt,@ppt)";
                        cmd.Parameters.AddWithValue("np", plate);
                        cmd.Parameters.AddWithValue("pt", place + ",");
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception e) { Console.WriteLine(e.ToString()); }
        }

        private void init()
        {
            IsValid = true;
#if (DEBUG)
            m_connection = new OleDbConnection(@"Provider=Microsoft.ACE.OLEDB.16.0;Data Source=C:\Users\pepuser\Desktop\polypeptideInfo.accdb;Persist Security Info=False;Jet OLEDB:Database Password=4919.skFI");
            m_connection.Open();
            DatabasePath = @"C:\Users\pepuser\Desktop\polypeptideInfo.accdb";
#elif (CUSTOMER)
            string path = @"\\c11w16fs01.genscript.com\int_17005\化学部\内部\分装发货信息\分装常用表格 备份\库存软件\polypeptideInfo.accdb";
            DatabasePath = path;
            string password= "cgxB1WJe7pU46KD5jpW/GQ==";
            try
            {
                password = $"Jet OLEDB:Database Password={NnConnection.NnDecrypt(password)};";
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                ShowMessage?.Invoke("数据库错误！", true);
                IsValid = false;
                return;
            }
            int version = 12;
            while (version < 21)
            {
                string connstr = $"Provider=Microsoft.ACE.OLEDB.{version}.0;Data Source={path};Persist Security Info=False;{password}";
                try
                {
                    m_connection = new OleDbConnection(connstr);
                    m_connection.Open();
                    return;
                }
                catch (Exception e) { Console.WriteLine(e.ToString() + "\n" + version); }

                ++version;
            }

            ShowMessage?.Invoke("数据库连接错误！建议检查数据库文件是否被改动。", true);
            IsValid = false;
            
#else
            string path = ConfigurationManager.AppSettings["stock_path"];
            DatabasePath = path;
            string password= ConfigurationManager.AppSettings["stock_psd"];
            if (path == null || password == null)
            {
                ShowMessage?.Invoke("配置文件错误，无法继续！", true);
                IsValid = false;
                return;
            }
            try
            {
                password = $"Jet OLEDB:Database Password={NnConnection.NnDecrypt(password)};";
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                ShowMessage?.Invoke("数据库错误！", true);
                IsValid = false;
                return;
            }
            int version = 12;
            while (version < 21)
            {
                string connstr = $"Provider=Microsoft.ACE.OLEDB.{version}.0;Data Source={path};Persist Security Info=False;{password}";
                try
                {
                    m_connection = new OleDbConnection(connstr);
                    m_connection.Open();
                    return;
                }
                catch (Exception e) { Console.WriteLine(e.ToString() + "\n" + version); }

                ++version;
            }

            ShowMessage?.Invoke("数据库连接错误！建议检查数据库文件是否被改动。", true);
            IsValid = false;
#endif
        }

        ~NnStockManager()
        {
            _deleteOtherFile();
            try
            {
                m_connection.Dispose();
                m_connection.Close();
            }
            catch { }
        }

        public static int GetIntFromDb(OleDbDataReader reader, string key)
        {
            int o = reader.GetOrdinal(key);
            if (!reader.IsDBNull(o))
                return reader.GetInt32(o);
            return 0;
        }

        public static double GetDoubleFromDb(OleDbDataReader reader, string key)
        {
            int o = reader.GetOrdinal(key);
            if (!reader.IsDBNull(o))
                return reader.GetDouble(o);
            return 0;
        }
        public static DateTime GetDateTiemFromDb(OleDbDataReader reader, string key)
        {
            int o = reader.GetOrdinal(key);
            if (!reader.IsDBNull(o))
                return (DateTime)reader[key];
            return DateTime.MinValue;
        }

        public static string GetStringFromDb(OleDbDataReader reader, string key)
        {
            int o = reader.GetOrdinal(key);
            if (!reader.IsDBNull(o))
                return reader.GetString(o);
            return "";
        }

        // 获得字符串中最大的数字
        public static double GetMaxValue(string str)
        {
            if (string.IsNullOrWhiteSpace(str)) return 0;
            str += '\0';
            double value = 0;
            int index = 0;
            for (int len = 0; len < str.Length; ++len)
            {
                char c = str[len];
                if ((c < '0' || c > '9') && c != '.')
                {
                    if (len > index)
                    {
                        double d;
                        value = (double.TryParse(str.Substring(index, len - index), out d) && d > value) ? d : value;
                    }
                    index = len + 1;
                }
            }
            return value;
        }
    }
}
