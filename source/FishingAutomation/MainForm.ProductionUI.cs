namespace FishingAutomation;

public sealed partial class MainForm
{
    private long _productionCurrentQuantity;
    private int _productionTargetQuantity;
    private string _productionDisplayName = "설정에서 선택";
    private string _productionFacilityName = "—";

    // Plain numeric text input shared by gathering/altering/crafting.
    // Using a TextBox instead of NumericUpDown removes the native spinner button
    // window completely, so there is no hidden-arrow gutter or rendering artifact.
    private sealed class QuantityTextBox : TextBox
    {
        private int _minimum = 1;
        private int _maximum = 1_000_000;
        private int _value = 100;
        private bool _syncing;

        internal int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = Math.Max(0, value);
                if (_maximum < _minimum) _maximum = _minimum;
                Value = _value;
            }
        }

        internal int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(_minimum, value);
                Value = _value;
            }
        }

        internal int Value
        {
            get => _value;
            set
            {
                int next = Math.Clamp(value, _minimum, _maximum);
                bool changed = next != _value;
                _value = next;
                string text = next.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!string.Equals(Text, text, StringComparison.Ordinal))
                {
                    _syncing = true;
                    Text = text;
                    SelectionStart = TextLength;
                    _syncing = false;
                }
                if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        internal event EventHandler? ValueChanged;

        internal QuantityTextBox()
        {
            BorderStyle = BorderStyle.None;
            TextAlign = HorizontalAlignment.Left;
            _syncing = true;
            Text = _value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _syncing = false;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
                return;
            }
            base.OnKeyPress(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (_syncing) return;
            if (!int.TryParse(Text, out int parsed) || parsed < _minimum || parsed > _maximum)
                return;
            if (parsed == _value) return;
            _value = parsed;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            if (!int.TryParse(Text, out int parsed))
                parsed = _value;
            Value = parsed;
        }
    }

    // V0.1.94 keeps gathering/altering inside the original reference dashboard.
    // These hooks now only refresh the shared dashboard; no second page is layered on top.
    private void UpdateProductionDashboardVisibility()
    {
        _referenceDashboard?.UpdateAbyssDungeonPickerVisibility();
        _referenceDashboard?.Invalidate();
    }

    private void RefreshProductionDashboard()
    {
        UpdateDashboard();
        _referenceDashboard?.Invalidate();
        if (_gatheringPage is null || _alteringPage is null || _craftingPage is null) return;
        _gatheringPage.UpdateExecution();
        _alteringPage.UpdateExecution();
        _craftingPage.UpdateExecution();
        if (_productionPageMode == "제작")
        {
            _ = RefreshCraftingStateAsync();
        }
        else if (_productionPageMode is not null && !_productionPolling && DateTime.UtcNow >= _productionNextPoll)
        {
            _ = RefreshProductionStateAsync(_productionPageMode == "가공" ? _alteringPage : _gatheringPage);
        }
    }
}
