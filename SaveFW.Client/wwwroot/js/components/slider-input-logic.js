window.SliderInputLogic = (function ()
{
    function init(rangeInput, textInput, container, config)
    {
        try
        {
            if (!rangeInput || !textInput || !container)
            {
                console.error("SliderInput: Missing elements", { rangeInput, textInput, container });
                return;
            }

            const min = Number(config.min) || 0;
            const max = Number(config.max) || 100;
            const range = max - min;
            const markers = config.markers || [];
            const decimals = config.decimalPlaces;
            const type = config.inputType;
            const snapThreshold = range * 0.02; // Snap when within 2% of marker

            // Inject CSS for slider thumb (pseudo-elements can't be styled inline)
            if (!document.getElementById('slider-input-styles'))
            {
                const style = document.createElement('style');
                style.id = 'slider-input-styles';
                style.textContent = `
                    input[type=range].slider-styled {
                        position: absolute;
                        top: 50%;
                        transform: translateY(-50%);
                        z-index: 40;
                        margin: 0;
                        padding: 0;
                        width: calc(100% + 16px); /* Extend logic width so thumb center reaches edges */
                        left: -8px; /* Offset to align thumb center with 0% */
                        -webkit-appearance: none;
                        appearance: none;
                        background: transparent;
                    }
                    input[type=range].slider-styled::-webkit-slider-thumb {
                        -webkit-appearance: none;
                        appearance: none;
                        width: 16px;
                        height: 16px;
                        border-radius: 50%;
                        background: #e2e8f0;
                        border: 2px solid #94a3b8;
                        cursor: pointer;
                    }
                    input[type=range].slider-styled::-moz-range-thumb {
                        width: 16px;
                        height: 16px;
                        border-radius: 50%;
                        background: #e2e8f0;
                        border: 2px solid #94a3b8;
                        cursor: pointer;
                    }
                    .slider-track {
                        position: absolute;
                        top: 50%;
                        left: 0;
                        width: 100%;
                        height: 0.5rem;
                        background-color: #334155;
                        border-radius: 0.5rem;
                        transform: translateY(-50%);
                        z-index: 10;
                        pointer-events: none;
                    }
                    .slider-tick {
                        position: absolute;
                        top: 50%;
                        transform: translate(-50%, -50%);
                        background-color: #94a3b8;
                        z-index: 20;
                        pointer-events: none;
                    }
                    .tick-major {
                        width: 2px;
                        height: 16px;
                        background-color: #cbd5e1;
                    }
                    .tick-minor {
                        width: 1px;
                        height: 16px;
                        opacity: 0.5;
                    }
                    .tick-label {
                        position: absolute;
                        top: 20px;
                        transform: translateX(-50%);
                        font-size: 0.65rem;
                        color: #94a3b8;
                        font-weight: 600;
                        white-space: nowrap;
                        pointer-events: none;
                        z-index: 35;
                    }
                `;
                document.head.appendChild(style);
            }

            // Apply styling class to range input
            rangeInput.classList.add('slider-styled');

            // Helpers
            const formatFn = (v) =>
            {
                if (type === 'Currency') return '$' + parseInt(v).toLocaleString();
                if (type === 'MoneyMM') return '$' + v + 'MM';
                if (type === 'Percent') return v + '%';
                return v;
            };

            const fmtNum = (n) => n.toLocaleString(undefined, {
                minimumFractionDigits: decimals,
                maximumFractionDigits: decimals
            });

            const clamp = (v) => Math.min(Math.max(v, min), max);

            // 1. Ticks
            const relativeContainer = rangeInput.parentElement;
            if (relativeContainer)
            {
                relativeContainer.querySelectorAll('.slider-tick, .tick-label').forEach(el => el.remove());

                const addTick = (v, typeClass, text = null) =>
                {
                    const pct = ((v - min) / range) * 100;
                    const tick = document.createElement('div');
                    tick.className = `slider-tick ${typeClass}`;
                    tick.style.left = `${pct}%`;
                    relativeContainer.insertBefore(tick, rangeInput);

                    if (text)
                    {
                        const lbl = document.createElement('div');
                        lbl.className = 'tick-label';
                        lbl.textContent = text;
                        lbl.style.left = `${pct}%`;
                        relativeContainer.insertBefore(lbl, rangeInput);
                    }
                };

                if (range > 0 && config.majorTickInterval > 0)
                {
                    const startMajor = Math.ceil(min / config.majorTickInterval) * config.majorTickInterval;
                    for (let v = startMajor; v <= max; v += config.majorTickInterval)
                    {
                        addTick(v, 'tick-major', formatFn(v));
                    }

                    if (config.minorTickInterval > 0)
                    {
                        const startMinor = Math.ceil(min / config.minorTickInterval) * config.minorTickInterval;
                        for (let v = startMinor; v <= max; v += config.minorTickInterval)
                        {
                            if (Math.abs(v % config.majorTickInterval) > 0.001) addTick(v, 'tick-minor');
                        }
                    }
                }
            }

            // 2. Markers
            if (relativeContainer)
            {
                // Remove old markers. Use group-lbl to avoid selector issues.
                relativeContainer.querySelectorAll('input[type="radio"], .group-lbl').forEach(el => el.remove());

                const markerColor = config.markerColor || "red";
                const gradRed = "radial-gradient(circle, #fff 35%, #fff 35%)";
                const gradGreen = "radial-gradient(circle, #fff 35%, #fff 35%)";
                const borderRed = "#fff";
                const borderGreen = "#fff";

                const activeGrad = markerColor === "green" ? gradGreen : gradRed;
                const activeBorder = markerColor === "green" ? borderGreen : borderRed;

                markers.forEach((marker) =>
                {
                    const pct = ((marker.value - min) / range) * 100;

                    // Align matches the simple percentage of tick markers. 
                    // Note: This may slightly misalign with the native slider thumb at the extremes if the thumb is inset,
                    // but it guarantees alignment with the visible tick marks.
                    const leftStyle = `${pct}%`;

                    // --- RADIO BUTTON ---
                    const radio = document.createElement('input'); // Create input element
                    radio.type = 'radio'; // Set as radio button
                    radio.name = rangeInput.id + '_marker'; // Group by slider ID
                    radio.style.position = 'absolute'; // Absolute positioning relative to container
                    radio.style.left = leftStyle; // Position horizontally based on value
                    radio.style.top = '50%'; // Center vertically
                    radio.style.transform = 'translate(-50%, -50%) scale(1.1)'; // Center origin and scale up slightly
                    radio.style.width = '16px'; // Set width
                    radio.style.height = '16px'; // Set height
                    radio.style.zIndex = '30'; // Ensure it sits above track but below tooltip
                    radio.style.pointerEvents = 'none'; // Click-through (purely visual)
                    radio.style.appearance = 'none'; // Remove browser default styling
                    radio.style.backgroundColor = '#0f172a'; // Dark background
                    radio.style.border = '2px solid #94a3b8'; // Light gray border
                    radio.style.borderRadius = '50%'; // Make it circular
                    radio.style.transition = 'all 0.2s'; // Smooth transition for state changes

                    const updateRadio = () =>
                    {
                        const isChecked = Math.abs(parseFloat(rangeInput.value) - marker.value) < (range * 0.005);
                        radio.checked = isChecked;
                        if (isChecked)
                        {
                            radio.style.backgroundImage = activeGrad;
                            radio.style.borderColor = activeBorder;
                            radio.style.transform = 'translate(-50%, -50%) scale(1.15)';
                        } else
                        {
                            radio.style.backgroundImage = 'none';
                            radio.style.borderColor = '#94a3b8';
                            radio.style.transform = 'translate(-50%, -50%) scale(1.0)';
                        }
                    };

                    relativeContainer.appendChild(radio);

                    // --- MARKER LINE & LABEL ---
                    const labelDiv = document.createElement('div');
                    labelDiv.className = "absolute flex flex-col items-center z-[29]";
                    labelDiv.style.left = leftStyle;
                    labelDiv.style.top = 'calc(50%)';
                    labelDiv.style.transform = 'translateX(-50%)';

                    labelDiv.innerHTML = `
                        <div style="height: 32px; width: 1px; background-color: rgba(100,116,139,0.5); margin-bottom: 4px; pointer-events: none;"></div>
                        <div class="marker-hover-trigger relative flex items-center gap-1 cursor-help hover:text-blue-500 transition-colors text-[10px] text-slate-400 uppercase font-bold tracking-wider whitespace-nowrap">
                            <span>${marker.label}</span>
                            <span class="material-symbols-outlined text-[12px]">info</span>
                        </div>
                    `;

                    relativeContainer.appendChild(labelDiv);

                    // Attach portal tooltip (renders at body level, escaping stacking contexts)
                    const trigger = labelDiv.querySelector('.marker-hover-trigger');
                    if (trigger && marker.tooltipDescription && window.TooltipPortal)
                    {
                        TooltipPortal.attach(trigger, marker.tooltipDescription);
                    }

                    rangeInput.addEventListener('input', updateRadio);
                    updateRadio();
                });
            }

            // 3. Logic - Define Handlers
            if (rangeInput._sliderHandlers)
            {
                textInput.removeEventListener('input', rangeInput._sliderHandlers.onTextInput);
                textInput.removeEventListener('blur', rangeInput._sliderHandlers.onTextCommit);
                textInput.removeEventListener('change', rangeInput._sliderHandlers.onTextCommit);
                rangeInput.removeEventListener('input', rangeInput._sliderHandlers.onRangeInput);
            }

            const onTextInput = (e) =>
            {
                const valStr = e.target.value;
                if (!valStr || valStr === '-') return;

                const raw = valStr.replace(/,/g, '');
                let v = parseFloat(raw);
                if (isNaN(v)) return;

                if (v > max)
                {
                    v = max;
                    textInput.value = fmtNum(v);
                }
            };

            const onTextCommit = () =>
            {
                const raw = textInput.value.replace(/,/g, '');
                let v = parseFloat(raw);
                if (isNaN(v)) v = min;

                const clamped = clamp(v);
                textInput.value = fmtNum(clamped);
                rangeInput.value = clamped;
                rangeInput.dispatchEvent(new Event('input', { bubbles: true }));
            };

            const onRangeInput = (e) =>
            {
                let v = parseFloat(e.target.value);

                // Snap to nearest marker if within threshold
                for (const marker of markers)
                {
                    if (Math.abs(v - marker.value) <= snapThreshold)
                    {
                        v = marker.value;
                        rangeInput.value = v;
                        break;
                    }
                }

                if (document.activeElement !== textInput)
                {
                    textInput.value = fmtNum(v);
                }
            };

            textInput.addEventListener('input', onTextInput);
            textInput.addEventListener('blur', onTextCommit);
            textInput.addEventListener('change', onTextCommit);
            rangeInput.addEventListener('input', onRangeInput);

            rangeInput._sliderHandlers = { onTextInput, onTextCommit, onRangeInput };

        } catch (err)
        {
            console.error("SliderInput Init Error:", err);
        }
    }

    return { init: init };
})();