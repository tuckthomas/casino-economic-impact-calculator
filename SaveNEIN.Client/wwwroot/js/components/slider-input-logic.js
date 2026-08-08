window.SliderInputLogic = (function ()
{
    function init(rangeInput, textInput, container, presetSelectOrConfig, configArg)
    {
        try
        {
            let presetSelect = presetSelectOrConfig;
            let config = configArg;

            const isElement = (value) => value && value.nodeType === 1;
            const isConfigLike = (value) =>
                value &&
                typeof value === "object" &&
                !isElement(value);

            if (!config && isConfigLike(presetSelectOrConfig))
            {
                config = presetSelectOrConfig;
                presetSelect = null;
            }

            config = config || {};

            if (!presetSelect && rangeInput && typeof rangeInput.id === "string" && rangeInput.id.startsWith("input-"))
            {
                const guessedPresetId = `preset-${rangeInput.id.substring("input-".length)}`;
                presetSelect = document.getElementById(guessedPresetId);
            }

            if (!presetSelect && container && typeof container.querySelector === "function")
            {
                presetSelect = container.querySelector("select[id^='preset-']");
            }

            if (!rangeInput || !textInput || !container)
            {
                console.error("SliderInput: Missing core elements", { rangeInput, textInput, container });
                return;
            }

            if (!presetSelect)
            {
                console.error("SliderInput: Missing elements", { rangeInput, textInput, container, presetSelect });
                return;
            }

            const min = Number(config.min ?? rangeInput.min ?? 0);
            const max = Number(config.max ?? rangeInput.max ?? 100);
            const decimals = Number.isFinite(Number(config.decimalPlaces)) ? Number(config.decimalPlaces) : 2;
            const type = config.inputType || 'Number';
            const step = Number(config.step ?? rangeInput.step ?? 0);
            const customOptionValue = String(config.customOptionValue || '__custom__');
            const numericTolerance = Math.max(Number.isFinite(step) && step > 0 ? step / 2 : 0, 0.000001);

            const fmtNum = (n) =>
            {
                return n.toLocaleString(undefined, {
                    minimumFractionDigits: decimals,
                    maximumFractionDigits: decimals,
                    useGrouping: type !== 'Number'
                });
            };

            const parseInputNumber = (rawValue) =>
            {
                const cleaned = String(rawValue || '')
                    .replace(/,/g, '')
                    .replace(/\$/g, '')
                    .replace(/MM/gi, '')
                    .replace(/%/g, '')
                    .trim();

                const parsed = parseFloat(cleaned);
                return Number.isFinite(parsed) ? parsed : NaN;
            };

            const clamp = (v) => Math.min(Math.max(v, min), max);
            const optionValues = Array.from(presetSelect.options)
                .map(option => ({
                    raw: option.value,
                    numeric: option.value === customOptionValue ? NaN : Number(option.value)
                }))
                .filter(option => option.raw === customOptionValue || Number.isFinite(option.numeric));

            const setReadOnlyMode = (isReadOnly) =>
            {
                textInput.readOnly = isReadOnly;
                textInput.classList.toggle('cursor-not-allowed', isReadOnly);
                textInput.classList.toggle('opacity-80', isReadOnly);
                textInput.classList.toggle('sfw-value-readonly', isReadOnly);
                textInput.classList.toggle('sfw-value-editable', !isReadOnly);
                textInput.setAttribute('aria-readonly', isReadOnly ? 'true' : 'false');
                textInput.title = isReadOnly ? 'Select Custom to edit' : '';
            };

            const emitSyncEvent = () =>
            {
                rangeInput.dispatchEvent(new CustomEvent('slider-input-sync', {
                    bubbles: true,
                    detail: {
                        inputId: rangeInput.id,
                        value: Number(rangeInput.value)
                    }
                }));
            };

            const findMatchingOption = (numericValue) =>
            {
                let best = null;
                let bestDelta = Number.POSITIVE_INFINITY;

                optionValues.forEach(option =>
                {
                    if (!Number.isFinite(option.numeric)) return;
                    const delta = Math.abs(option.numeric - numericValue);
                    if (delta <= numericTolerance && delta < bestDelta)
                    {
                        best = option;
                        bestDelta = delta;
                    }
                });

                return best;
            };

            const applyFromRangeValue = (preferMatch = true) =>
            {
                let numericValue = parseInputNumber(rangeInput.value);
                if (!Number.isFinite(numericValue))
                {
                    const defaultValue = Number(config.defaultValue ?? rangeInput.dataset.default ?? min);
                    numericValue = Number.isFinite(defaultValue) ? defaultValue : min;
                }

                numericValue = clamp(numericValue);
                rangeInput.value = numericValue.toFixed(decimals);

                if (document.activeElement !== textInput || textInput.readOnly)
                {
                    textInput.value = fmtNum(numericValue);
                }

                // If user has selected Custom, preserve that choice across re-inits/rerenders.
                // Do not auto-snap back to a nearest preset unless the user explicitly picks one.
                if (presetSelect.value === customOptionValue)
                {
                    setReadOnlyMode(false);
                    return;
                }

                const match = findMatchingOption(numericValue);
                if (match)
                {
                    presetSelect.value = match.raw;
                    setReadOnlyMode(true);
                }
                else
                {
                    presetSelect.value = customOptionValue;
                    setReadOnlyMode(false);
                }
            };

            if (rangeInput._sliderHandlers)
            {
                textInput.removeEventListener('input', rangeInput._sliderHandlers.onTextInput);
                textInput.removeEventListener('blur', rangeInput._sliderHandlers.onTextCommit);
                textInput.removeEventListener('change', rangeInput._sliderHandlers.onTextCommit);
                rangeInput.removeEventListener('input', rangeInput._sliderHandlers.onRangeInput);
                presetSelect.removeEventListener('change', rangeInput._sliderHandlers.onPresetChange);
                delete presetSelect._sliderOnPresetChange;
            }

            const onTextInput = (e) =>
            {
                if (textInput.readOnly) return;

                const numericValue = parseInputNumber(e.target.value);
                if (!Number.isFinite(numericValue))
                {
                    return;
                }

                if (numericValue > max)
                {
                    textInput.value = fmtNum(max);
                }
            };

            const onTextCommit = () =>
            {
                if (textInput.readOnly)
                {
                    applyFromRangeValue(false);
                    return;
                }

                let numericValue = parseInputNumber(textInput.value);
                if (!Number.isFinite(numericValue))
                {
                    numericValue = parseInputNumber(rangeInput.value);
                }
                if (!Number.isFinite(numericValue))
                {
                    numericValue = min;
                }

                numericValue = clamp(numericValue);
                rangeInput.value = numericValue.toFixed(decimals);
                textInput.value = fmtNum(numericValue);

                applyFromRangeValue(false);

                rangeInput.dispatchEvent(new Event('input', { bubbles: true }));
                rangeInput.dispatchEvent(new Event('change', { bubbles: true }));
                emitSyncEvent();
            };

            const onRangeInput = (e) =>
            {
                applyFromRangeValue(false);
                emitSyncEvent();
            };

            const onPresetChange = () =>
            {
                const selected = presetSelect.value;
                if (selected === customOptionValue)
                {
                    setReadOnlyMode(false);
                    textInput.focus();
                    textInput.select();
                    return;
                }

                const numericValue = Number(selected);
                if (!Number.isFinite(numericValue))
                {
                    return;
                }

                rangeInput.value = clamp(numericValue).toFixed(decimals);
                applyFromRangeValue(true);
                rangeInput.dispatchEvent(new Event('input', { bubbles: true }));
                rangeInput.dispatchEvent(new Event('change', { bubbles: true }));
                emitSyncEvent();
            };

            textInput.addEventListener('input', onTextInput);
            textInput.addEventListener('blur', onTextCommit);
            textInput.addEventListener('change', onTextCommit);
            rangeInput.addEventListener('input', onRangeInput);
            presetSelect.addEventListener('change', onPresetChange);

            rangeInput._sliderHandlers = { onTextInput, onTextCommit, onRangeInput, onPresetChange };
            presetSelect._sliderOnPresetChange = onPresetChange;

            applyFromRangeValue(true);

        } catch (err)
        {
            console.error("SliderInput Init Error:", err);
        }
    }

    function setPresetValue(presetSelect, value)
    {
        try
        {
            if (!presetSelect)
            {
                return;
            }

            const nextValue = String(value ?? '');
            if (presetSelect.value !== nextValue)
            {
                presetSelect.value = nextValue;
            }

            if (typeof presetSelect._sliderOnPresetChange === "function")
            {
                presetSelect._sliderOnPresetChange();
                return;
            }

            presetSelect.dispatchEvent(new Event('change', { bubbles: true }));
        }
        catch (err)
        {
            console.error("SliderInput setPresetValue Error:", err);
        }
    }

    return {
        init: init,
        setPresetValue: setPresetValue
    };
})();
