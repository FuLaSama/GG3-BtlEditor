/*
 * NullableNumericUpDown.cs
 *
 * 可空数字框：空文本 = null，对应「这个 FB 槽没写 / 用户清空」。
 * 有数字则 SetScalar 写入（包括 0，0 会留下槽）。
 * 高度固定，避免 TableLayout 行高跟着字号跳。
 */
using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BtldMapEditor
{
    /// <summary>
    /// 基于 TextBox 的可空数值编辑；高度固定，避免 TableLayout 行高跳变。
    /// </summary>
    public class NullableNumericUpDown : TextBox
    {
        private decimal _minimum = -32768;
        private decimal _maximum = 65535;
        private decimal? _lastFiredValue = null;
        private bool _isInternalUpdating = false;
        private bool _userEnabled = true;
        private string _shownText = "";
        private object _shownValue;

        public event EventHandler ValueChanged;

        public NullableNumericUpDown()
        {
            this.TextAlign = HorizontalAlignment.Left;
            this.BorderStyle = BorderStyle.Fixed3D;
            this.AutoSize = false;
            this.Height = Math.Max(26, Font.Height + 10);
            UpdateVisualState();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            this.Height = Math.Max(26, Font.Height + 10);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int h = Math.Max(26, Font.Height + 10);
            Size s = base.GetPreferredSize(proposedSize);
            return new Size(s.Width, h);
        }

        public new bool Enabled
        {
            get => _userEnabled;
            set
            {
                _userEnabled = value;
                base.TabStop = value;
                UpdateVisualState();
            }
        }

        private void UpdateVisualState()
        {
            if (_userEnabled)
            {
                this.BackColor = SystemColors.Window;
                this.ForeColor = SystemColors.WindowText;
                this.Cursor = Cursors.IBeam;
            }
            else
            {
                this.BackColor = SystemColors.Control;
                this.ForeColor = SystemColors.GrayText;
                this.Cursor = Cursors.Default;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!_userEnabled)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!_userEnabled)
            {
                e.Handled = true;
                return;
            }
            base.OnKeyPress(e);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CONTEXTMENU = 0x007B;
            const int WM_PASTE = 0x0302;
            const int WM_CUT = 0x0300;
            const int WM_CLEAR = 0x0301;

            if (!_userEnabled)
            {
                if (m.Msg == WM_CONTEXTMENU || m.Msg == WM_PASTE || m.Msg == WM_CUT || m.Msg == WM_CLEAR)
                {
                    return;
                }
            }
            base.WndProc(ref m);
        }

        [Category("Data")]
        public decimal Minimum
        {
            get => _minimum;
            set => _minimum = value;
        }

        [Category("Data")]
        public decimal Maximum
        {
            get => _maximum;
            set => _maximum = value;
        }

        [Category("Data")]
        public int DecimalPlaces { get; set; } = 0;

        [Category("Data")]
        public decimal Increment { get; set; } = 1;

        [Category("Data")]
        public decimal Value
        {
            get => NullableValue ?? Minimum;
            set => NullableValue = value;
        }

        [Category("Data")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal? NullableValue
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Text)) return null;
                if (decimal.TryParse(Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal val))
                {
                    return val;
                }
                return null;
            }
            set
            {
                SetDisplayValue(value);
            }
        }

        /// <summary>显示原值，不按编辑范围截断。未编辑时读回原对象，保留浮点精度。</summary>
        public void SetDisplayValue(object value)
        {
            _shownValue = value;
            _shownText = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            _isInternalUpdating = true;
            try { Text = _shownText; }
            finally { _isInternalUpdating = false; }
            _lastFiredValue = NullableValue;
        }

        /// <summary>只有空白才表示 null；非法、越界或非整数输入不能被提交为缺省。</summary>
        public object ReadValue()
        {
            if (Text == _shownText) return _shownValue;
            if (string.IsNullOrWhiteSpace(Text)) return null;
            if (!decimal.TryParse(Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
                throw new FormatException("请输入有效数字；留空才表示未写。");
            if (DecimalPlaces == 0 && decimal.Truncate(value) != value)
                throw new FormatException("这个字段需要整数。");
            if (value < Minimum || value > Maximum)
                throw new FormatException($"数值必须在 {Minimum} ～ {Maximum} 之间。");
            return (double)value;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (!_isInternalUpdating)
            {
                CheckAndFireValueChanged();
            }
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            CheckAndFireValueChanged();
        }

        private void CheckAndFireValueChanged()
        {
            // 无效文本保持原样，不发出一个看似“清空”的事件。
            try { _ = ReadValue(); }
            catch (FormatException) { return; }
            decimal? current = NullableValue;
            if (current != _lastFiredValue)
            {
                _lastFiredValue = current;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
