using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ModularFlightPanel.UI.Framework
{
    /// <summary>
    /// 遥测综合矩阵卡展示模式 (MechJeb 风格)
    /// </summary>
    public enum MatrixDisplayMode
    {
        /// <summary>
        /// 键值网格模式：每个单元格包含独立标签 (居左) 与数值读数 (居右)，支持 1~6 列及任意多行。
        /// 1 列时为标准 MechJeb 纵向信息清单；2 列时为双列对称监控。
        /// </summary>
        KeyValue,

        /// <summary>
        /// 数据表格模式：顶部为各列标题表头，下方为多行数据网格。
        /// 完美复刻 MechJeb 分级 Delta-V 状态表格。
        /// </summary>
        Table
    }

    /// <summary>
    /// 单元格数据模型
    /// </summary>
    public class MatrixCellData
    {
        public string Label = "";
        public string Token = "";

        public MatrixCellData() { }

        public MatrixCellData(string label, string token)
        {
            Label = label ?? "";
            Token = token ?? "";
        }
    }

    /// <summary>
    /// 遥测综合矩阵卡完整数据模型与序列化编排器 (Telemetry Matrix Data Orchestrator)
    /// 负责在 CustomTemplate、运行时 UGUI 渲染与设置面板之间提供零依赖、双向无损转换。
    /// </summary>
    public class TelemetryMatrixData
    {
        public MatrixDisplayMode Mode = MatrixDisplayMode.KeyValue;
        public int Columns = 2;
        public int Rows = 3;
        public List<string> TableHeaders = new List<string>();
        public List<List<MatrixCellData>> Grid = new List<List<MatrixCellData>>();

        public static TelemetryMatrixData CreateDefaultKeyValue()
        {
            var data = new TelemetryMatrixData
            {
                Mode = MatrixDisplayMode.KeyValue,
                Columns = 2,
                Rows = 3
            };

            // Row 0: 空速 | 海拔真高
            var r0 = new List<MatrixCellData>
            {
                new MatrixCellData("SPD", "{SPD}"),
                new MatrixCellData("ASL", "{ALT:ASL:DIST}")
            };
            // Row 1: 雷达高 | 动压
            var r1 = new List<MatrixCellData>
            {
                new MatrixCellData("RALT", "{ALT:AGL:DIST}"),
                new MatrixCellData("Q", "{Q}")
            };
            // Row 2: 垂直升降 | 重力过载
            var r2 = new List<MatrixCellData>
            {
                new MatrixCellData("VSI", "{VSI}"),
                new MatrixCellData("G", "{GFORCE}")
            };

            data.Grid.Add(r0);
            data.Grid.Add(r1);
            data.Grid.Add(r2);
            data.EnsureGridDimensions();
            return data;
        }

        public static TelemetryMatrixData CreateSingleColumnMonitor()
        {
            var data = new TelemetryMatrixData
            {
                Mode = MatrixDisplayMode.KeyValue,
                Columns = 1,
                Rows = 6
            };

            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("分级 ΔV", "{STAGE:DV}") });
            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("总计 ΔV", "{TOTALDV}") });
            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("地表速度", "{SPD:SURF}") });
            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("绝对海拔", "{ALT:ASL:DIST}") });
            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("实时推重比", "{TWR}") });
            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("气动动压", "{Q}") });

            data.EnsureGridDimensions();
            return data;
        }

        public static TelemetryMatrixData CreateDefaultTable()
        {
            var data = new TelemetryMatrixData
            {
                Mode = MatrixDisplayMode.Table,
                Columns = 9,
                Rows = 1
            };

            data.TableHeaders.AddRange(new[]
            {
                "Stage", "初始质量", "最终质量", "推重比", "最大推重比", "海平面推重比", "大气内 ΔV", "真空 ΔV", "燃烧时间"
            });

            var row0 = new List<MatrixCellData>
            {
                new MatrixCellData("", "{STAGE}"),
                new MatrixCellData("", "{VESSEL:MASS}"),
                new MatrixCellData("", "{VESSEL:DRYMASS}"),
                new MatrixCellData("", "{TWR}"),
                new MatrixCellData("", "{TWR:MAX}"),
                new MatrixCellData("", "{TWR:SL}"),
                new MatrixCellData("", "{STAGE:DV:ATM}"),
                new MatrixCellData("", "{STAGE:DV}"),
                new MatrixCellData("", "{STAGE:BURNTIME}")
            };
            data.Grid.Add(row0);
            data.EnsureGridDimensions();
            return data;
        }

        public static TelemetryMatrixData CreateOrbitalMatrix()
        {
            var data = new TelemetryMatrixData
            {
                Mode = MatrixDisplayMode.KeyValue,
                Columns = 2,
                Rows = 4
            };

            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("远拱点", "{AP:DIST}"), new MatrixCellData("近拱点", "{PE:DIST}") });
            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("轨道倾角", "{INC}"), new MatrixCellData("偏心率", "{ECC}") });
            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("节点 ΔV", "{NODE:DV}"), new MatrixCellData("节点倒计", "{NODE:TIME}") });
            data.Grid.Add(new List<MatrixCellData> { new MatrixCellData("轨道周期", "{PERIOD}"), new MatrixCellData("真近点角", "{TRA}") });

            data.EnsureGridDimensions();
            return data;
        }

        public static TelemetryMatrixData FromTemplate(string rawTemplate)
        {
            if (string.IsNullOrEmpty(rawTemplate))
            {
                return CreateDefaultKeyValue();
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int start = 0;
            int len = rawTemplate.Length;
            while (start < len)
            {
                int semi = rawTemplate.IndexOf(';', start);
                if (semi < 0) semi = len;
                int eq = rawTemplate.IndexOf('=', start);
                if (eq > start && eq < semi)
                {
                    string k = rawTemplate.Substring(start, eq - start).Trim();
                    string v = rawTemplate.Substring(eq + 1, semi - (eq + 1)).Trim();
                    if (k.Length > 0) dict[k] = v;
                }
                start = semi + 1;
            }

            // 判断是否存在结构化行与列标记
            if (dict.TryGetValue("COLS", out string colsStr) && int.TryParse(colsStr, out int cols))
            {
                var data = new TelemetryMatrixData
                {
                    Columns = Mathf.Clamp(cols, 1, 12),
                    Rows = 1
                };

                if (dict.TryGetValue("ROWS", out string rowsStr) && int.TryParse(rowsStr, out int rows))
                {
                    data.Rows = Mathf.Clamp(rows, 1, 20);
                }

                if (dict.TryGetValue("MODE", out string modeStr) && modeStr.Equals("TABLE", StringComparison.OrdinalIgnoreCase))
                {
                    data.Mode = MatrixDisplayMode.Table;
                }
                else
                {
                    data.Mode = MatrixDisplayMode.KeyValue;
                }

                if (data.Mode == MatrixDisplayMode.Table)
                {
                    for (int c = 0; c < data.Columns; c++)
                    {
                        dict.TryGetValue($"COL{c}_HDR", out string hdr);
                        data.TableHeaders.Add(string.IsNullOrEmpty(hdr) ? $"Col {c + 1}" : hdr);
                    }

                    for (int r = 0; r < data.Rows; r++)
                    {
                        var rowList = new List<MatrixCellData>();
                        for (int c = 0; c < data.Columns; c++)
                        {
                            dict.TryGetValue($"R{r}C{c}_VAL", out string val);
                            rowList.Add(new MatrixCellData("", val ?? ""));
                        }
                        data.Grid.Add(rowList);
                    }
                }
                else
                {
                    for (int r = 0; r < data.Rows; r++)
                    {
                        var rowList = new List<MatrixCellData>();
                        for (int c = 0; c < data.Columns; c++)
                        {
                            dict.TryGetValue($"R{r}C{c}_LBL", out string lbl);
                            dict.TryGetValue($"R{r}C{c}_VAL", out string val);
                            rowList.Add(new MatrixCellData(lbl ?? $"CH{r * data.Columns + c + 1}", val ?? ""));
                        }
                        data.Grid.Add(rowList);
                    }
                }

                data.EnsureGridDimensions();
                return data;
            }

            // 传统旧版配置平滑向前兼容 (CH1..CH6 映射为 2 列 x 3 行 MechJeb 矩阵)
            var legacyData = new TelemetryMatrixData
            {
                Mode = MatrixDisplayMode.KeyValue,
                Columns = 2,
                Rows = 3
            };

            string ch1 = dict.TryGetValue("CH1", out string v1) ? v1 : "{SPD}";
            string ch2 = dict.TryGetValue("CH2", out string v2) ? v2 : "{ALT:ASL:DIST}";
            string ch3 = dict.TryGetValue("CH3", out string v3) ? v3 : "{ALT:AGL:DIST}";
            string ch4 = dict.TryGetValue("CH4", out string v4) ? v4 : "{Q}";
            string ch5 = dict.TryGetValue("CH5", out string v5) ? v5 : "{VSI}";
            string ch6 = dict.TryGetValue("CH6", out string v6) ? v6 : "{GFORCE}";

            string ch2Lbl = "TWR";
            if (ch2.IndexOf("ASL", StringComparison.OrdinalIgnoreCase) >= 0) ch2Lbl = "ASL";
            else if (ch2.IndexOf("ALT", StringComparison.OrdinalIgnoreCase) >= 0) ch2Lbl = "ALT";

            legacyData.Grid.Add(new List<MatrixCellData> { new MatrixCellData("SPD", ch1), new MatrixCellData(ch2Lbl, ch2) });
            legacyData.Grid.Add(new List<MatrixCellData> { new MatrixCellData("RALT", ch3), new MatrixCellData("Q", ch4) });
            legacyData.Grid.Add(new List<MatrixCellData> { new MatrixCellData("VSI", ch5), new MatrixCellData("G", ch6) });

            legacyData.EnsureGridDimensions();
            return legacyData;
        }

        public string ToTemplate()
        {
            EnsureGridDimensions();
            var sb = new StringBuilder();
            sb.Append("MODE=").Append(Mode == MatrixDisplayMode.Table ? "TABLE" : "KV").Append(';');
            sb.Append("COLS=").Append(Columns).Append(';');
            sb.Append("ROWS=").Append(Rows).Append(';');

            if (Mode == MatrixDisplayMode.Table)
            {
                for (int c = 0; c < Columns; c++)
                {
                    string h = (c < TableHeaders.Count) ? TableHeaders[c] : $"Col {c + 1}";
                    sb.Append($"COL{c}_HDR=").Append(h ?? "").Append(';');
                }

                for (int r = 0; r < Rows; r++)
                {
                    for (int c = 0; c < Columns; c++)
                    {
                        string val = Grid[r][c]?.Token ?? "";
                        sb.Append($"R{r}C{c}_VAL=").Append(val).Append(';');
                    }
                }
            }
            else
            {
                for (int r = 0; r < Rows; r++)
                {
                    for (int c = 0; c < Columns; c++)
                    {
                        var cell = Grid[r][c];
                        sb.Append($"R{r}C{c}_LBL=").Append(cell?.Label ?? "").Append(';');
                        sb.Append($"R{r}C{c}_VAL=").Append(cell?.Token ?? "").Append(';');
                    }
                }
            }

            return sb.ToString();
        }

        public void EnsureGridDimensions()
        {
            Columns = Mathf.Clamp(Columns, 1, 12);
            Rows = Mathf.Clamp(Rows, 1, 20);

            while (Grid.Count < Rows)
            {
                var newRow = new List<MatrixCellData>();
                for (int c = 0; c < Columns; c++)
                {
                    newRow.Add(new MatrixCellData($"CH{Grid.Count * Columns + c + 1}", ""));
                }
                Grid.Add(newRow);
            }
            while (Grid.Count > Rows)
            {
                Grid.RemoveAt(Grid.Count - 1);
            }

            for (int r = 0; r < Rows; r++)
            {
                var rowList = Grid[r];
                while (rowList.Count < Columns)
                {
                    rowList.Add(new MatrixCellData($"CH{r * Columns + rowList.Count + 1}", ""));
                }
                while (rowList.Count > Columns)
                {
                    rowList.RemoveAt(rowList.Count - 1);
                }
            }

            while (TableHeaders.Count < Columns)
            {
                TableHeaders.Add($"Col {TableHeaders.Count + 1}");
            }
            while (TableHeaders.Count > Columns)
            {
                TableHeaders.RemoveAt(TableHeaders.Count - 1);
            }
        }

        public void AddRow()
        {
            Rows = Mathf.Min(20, Rows + 1);
            EnsureGridDimensions();
        }

        public void RemoveRow(int rowIndex)
        {
            if (Rows <= 1) return;
            if (rowIndex >= 0 && rowIndex < Grid.Count)
            {
                Grid.RemoveAt(rowIndex);
                Rows = Grid.Count;
                EnsureGridDimensions();
            }
        }

        public void MoveRow(int from, int to)
        {
            if (from < 0 || from >= Grid.Count || to < 0 || to >= Grid.Count || from == to) return;
            var item = Grid[from];
            Grid.RemoveAt(from);
            Grid.Insert(to, item);
        }

        public void SetColumns(int newColCount)
        {
            Columns = Mathf.Clamp(newColCount, 1, 12);
            EnsureGridDimensions();
        }
    }
}
