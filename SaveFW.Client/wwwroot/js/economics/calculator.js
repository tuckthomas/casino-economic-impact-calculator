window.EconomicCalculator = (function ()
{
    // Track initialization state
    let isInitialized = false;
    let isSyncing = false;

    const getCountyData = () => window.CurrentCountyList || [];

    let currentPop = 0;
    let lastImpactBreakdown = null;
    let otherCountiesExpanded = false;
    let lastCalculationResult = null;

    function escapeHtml(input)
    {
        return String(input ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll("\"", "&quot;")
            .replaceAll("'", "&#039;");
    }

    function fmtM(value)
    {
        const v = Number(value) || 0;
        return '$' + (v / 1000000).toFixed(1) + 'MM';
    }

    function fmtDiffM(value)
    {
        const v = Number(value) || 0;
        const sign = v < 0 ? '-' : '';
        return `${sign}${fmtM(Math.abs(v))}`;
    }

    // DOM element references - populated by init()
    let els = {};

    // Populate DOM element references - called during init()
    function initElements()
    {
        els = {
            // inCounty removed - managed by Map state
            inRevenue: document.getElementById('input-revenue'),
            inAGR: document.getElementById('input-agr'),
            inRate: document.getElementById('input-rate'),
            inAllocation: document.getElementById('input-allocation'),

            // Cost Inputs
            inCostCrime: document.getElementById('input-cost-crime'),
            inCostBusiness: document.getElementById('input-cost-business'),
            inCostBankruptcy: document.getElementById('input-cost-bankruptcy'),
            inCostIllness: document.getElementById('input-cost-illness'),
            inCostServices: document.getElementById('input-cost-services'),
            inCostAbused: document.getElementById('input-cost-abused'),

            // Allocation Visuals
            allocBarHuman: document.getElementById('alloc-bar-human'),
            allocBarTaxpayer: document.getElementById('alloc-bar-taxpayer'),
            allocTextHuman: document.getElementById('alloc-text-human'),
            allocTextTaxpayer: document.getElementById('alloc-text-taxpayer'),

            valRevenue: document.getElementById('val-revenue'),
            valAGR: document.getElementById('val-agr'),
            valEffectiveRate: document.getElementById('val-effective-rate'),
            valRate: document.getElementById('val-rate'),

            // Value Displays
            valCostTotal: document.getElementById('val-cost-total'),
            valCostCrime: document.getElementById('val-cost-crime'),
            valCostBusiness: document.getElementById('val-cost-business'),
            valCostBankruptcy: document.getElementById('val-cost-bankruptcy'),
            valCostIllness: document.getElementById('val-cost-illness'),
            valCostServices: document.getElementById('val-cost-services'),
            valCostAbused: document.getElementById('val-cost-abused'),
            resVictims: document.getElementById('res-victims'),
            resTotalCost: document.getElementById('res-total-cost'),
            resDeficit: document.getElementById('res-deficit'),
            resResultLabel: document.getElementById('res-result-label'),
            resEqRevenue: document.getElementById('res-eq-revenue'),
            resEqCost: document.getElementById('res-eq-cost'),
            resBar: document.getElementById('res-bar'),
            resFooter: document.getElementById('res-footer'),
            deficitTitle: document.getElementById('deficit-title'),

            // Calculation Breakdowns
            calcAGR: document.getElementById('calc-agr'),
            calcTaxRate: document.getElementById('calc-tax-rate'),
            calcTaxTotal: document.getElementById('calc-tax-total'),

            // Split Allocation Elements
            calcRevTotalH: document.getElementById('calc-rev-total-h'),
            calcAllocPctH: document.getElementById('calc-alloc-pct-h'),
            calcAllocHumanVal: document.getElementById('calc-alloc-human-val'),

            calcRevTotalG: document.getElementById('calc-rev-total-g'),
            calcAllocPctG: document.getElementById('calc-alloc-pct-g'),
            calcAllocGenVal: document.getElementById('calc-alloc-gen-val'),

            // Detailed Breakdown Elements
            calcBreakHealthVictims: document.getElementById('calc-break-health-victims'),
            calcBreakSocialVictims: document.getElementById('calc-break-social-victims'),
            calcBreakCrimeVictims: document.getElementById('calc-break-crime-victims'),
            calcBreakLegalVictims: document.getElementById('calc-break-legal-victims'),
            calcBreakEconVictims: document.getElementById('calc-break-econ-victims'),
            calcBreakTotalVictims: document.getElementById('calc-break-total-victims')
        };
    }

    // Init Counties
    function initCounties()
    {
        // 1. Populate Native Select (Hidden)
        els.inCounty.innerHTML = ''; // Clear
        const data = getCountyData();
        if (!data.length)
        {
            const opt = document.createElement('option');
            opt.value = '';
            opt.textContent = 'Select a state first';
            els.inCounty.appendChild(opt);
            const display = document.getElementById('county-display');
            if (display) display.textContent = 'Select a state first';
            renderCustomOptions([]);
            return;
        }

        data.forEach(c =>
        {
            const opt = document.createElement('option');
            opt.value = c.geoid || c.id || '';
            opt.dataset.pop = c.pop || 0;
            opt.textContent = c.pop ? `${c.name} (${c.pop.toLocaleString()})` : c.name;
            els.inCounty.appendChild(opt);
        });

        // 2. Init Custom UI
        renderCustomOptions(data); // Initial Render
    }

    // Render Custom Options Function
    function renderCustomOptions(data)
    {
        const container = document.getElementById('county-options');
        container.innerHTML = '';

        if (data.length === 0)
        {
            container.innerHTML = `<div class="p-4 text-center text-sm text-slate-400">No counties found.</div>`;
            return;
        }

        const sorted = [...data].sort((a, b) =>
        {
            if (sortMode === 'pop')
            {
                return sortDir === 'asc' ? (a.pop || 0) - (b.pop || 0) : (b.pop || 0) - (a.pop || 0);
            }
            return sortDir === 'asc' ? (a.name || '').localeCompare(b.name || '') : (b.name || '').localeCompare(a.name || '');
        });

        sorted.forEach(c =>
        {
            const div = document.createElement('div');
            div.className = "px-4 py-3 text-sm text-slate-200 hover:bg-slate-800 hover:text-blue-300 cursor-pointer transition-colors flex items-center justify-between group";
            div.innerHTML = `
                                        <span class="font-medium">${c.name}</span>
                                        <span class="text-xs text-white font-mono bg-[#0f172a] dark:bg-[#0f172a] px-2 py-0.5 rounded transition-colors">${c.pop ? c.pop.toLocaleString() : ''}</span>
                                    `;
            div.onclick = () =>
            {
                selectCounty(c.name, c.geoid || c.id || '', c.pop || 0);
            };
            container.appendChild(div);
        });
    }

    // Select County Helper
    function selectCounty(name, geoid, pop)
    {
        // 1. Update Native Select
        const didChange = !!(geoid && els.inCounty && els.inCounty.value !== geoid);
        if (didChange) els.inCounty.value = geoid;

        // 2. Update Display
        const display = document.getElementById('county-display');
        if (display) display.textContent = pop ? `${name} (${pop.toLocaleString()})` : name;

        // 3. Trigger Calculation
        if (didChange) els.inCounty.dispatchEvent(new Event('change'));

        // 4. Close Menu
        toggleMenu(false);
    }

    // Menu Logic - elements populated by initMenuLogic()
    let trigger = null;
    let menu = null;
    let searchInput = null;
    let sortAlphaBtn = null;
    let sortPopBtn = null;
    let isOpen = false;
    let sortMode = 'alpha';
    let sortDir = 'asc';

    function toggleMenu(show)
    {
        if (!menu || !searchInput) return;
        isOpen = show;
        if (show)
        {
            menu.classList.remove('hidden');
            // Small delay to allow display:block to apply before transition
            requestAnimationFrame(() =>
            {
                menu.classList.remove('opacity-0', 'scale-95');
                menu.classList.add('opacity-100', 'scale-100');
                searchInput.focus();
            });
        } else
        {
            menu.classList.remove('opacity-100', 'scale-100');
            menu.classList.add('opacity-0', 'scale-95');
            setTimeout(() =>
            {
                menu.classList.add('hidden');
                searchInput.value = ''; // Reset search
                renderCustomOptions(getCountyData()); // Reset list
            }, 200); // Match duration
        }
    }

    // Initialize menu logic - called during init()
    function initMenuLogic()
    {
        trigger = document.getElementById('county-trigger');
        menu = document.getElementById('county-menu');
        searchInput = document.getElementById('county-search');
        sortAlphaBtn = document.getElementById('county-sort-alpha');
        sortPopBtn = document.getElementById('county-sort-pop');

        if (trigger)
        {
            trigger.onclick = (e) =>
            {
                e.preventDefault(); // Prevent form submission if in form
                if (!getCountyData().length) return;
                toggleMenu(!isOpen);
            };
        }

        if (searchInput)
        {
            // Search Logic
            searchInput.oninput = (e) =>
            {
                const term = e.target.value.toLowerCase();
                const filtered = getCountyData().filter(c => c.name.toLowerCase().includes(term));
                renderCustomOptions(filtered);
            };
        }

        if (sortAlphaBtn)
        {
            sortAlphaBtn.onclick = (e) =>
            {
                e.preventDefault();
                if (sortMode === 'alpha')
                {
                    sortDir = sortDir === 'asc' ? 'desc' : 'asc';
                } else
                {
                    sortMode = 'alpha';
                    sortDir = 'asc';
                }
                renderCustomOptions(getCountyData());
            };
        }
        if (sortPopBtn)
        {
            sortPopBtn.onclick = (e) =>
            {
                e.preventDefault();
                if (sortMode === 'pop')
                {
                    sortDir = sortDir === 'asc' ? 'desc' : 'asc';
                } else
                {
                    sortMode = 'pop';
                    sortDir = 'desc';
                }
                renderCustomOptions(getCountyData());
            };
        }

        // Click Outside to Close
        document.addEventListener('click', (e) =>
        {
            if (trigger && menu && !trigger.contains(e.target) && !menu.contains(e.target) && isOpen)
            {
                toggleMenu(false);
            }
        });
    }


    function calculateTax(agr)
    {
        // Step A: Supplemental Tax
        let supplementalTax = agr * 0.035;

        // Step B: Determine Base Rate
        let baseRate = (agr < 75000000) ? 0.05 : 0.15;

        // Step C: Free Play Deduction
        let taxableAGR = Math.max(0, agr - 7000000);
        let freePlayDeduction = (agr > 7000000) ? 7000000 : agr;

        // Step D: Apply Brackets to Taxable_AGR
        let bracketTax = 0;
        let breakdown = [];

        // Format helper
        const fmtM = (v) => '$' + (v / 1000000).toFixed(1) + 'MM';
        const fmt = (v) => '$' + v.toLocaleString(undefined, { maximumFractionDigits: 0 });

        breakdown.push({ label: "Adjusted Gross Revenue (AGR)", val: fmt(agr), note: "", type: 'header' });
        breakdown.push({ label: "Supplemental Tax (3.5%)", val: fmt(supplementalTax), note: "Off the top", type: 'add' });
        breakdown.push({ label: "Free Play Deduction", val: fmt(freePlayDeduction), note: "First $7MM Exempt", type: 'info' });
        breakdown.push({ label: "Taxable AGR", val: fmt(taxableAGR), note: "AGR - $7MM", type: 'sub-header' });

        let remaining = taxableAGR;

        // Tier 1: First $25M
        let tier1Amt = Math.min(Math.max(0, remaining), 25000000);
        let t1Tax = tier1Amt * baseRate;
        bracketTax += t1Tax;
        remaining -= tier1Amt;
        breakdown.push({ label: `Tier 1 ($0-$25MM) @ ${(baseRate * 100).toFixed(0)}%`, val: fmt(t1Tax), note: `on ${fmtM(tier1Amt)}`, type: 'add' });

        // Tier 2: Next $25M ($25M-$50M) @ 20%
        let tier2Amt = Math.min(Math.max(0, remaining), 25000000);
        let t2Tax = tier2Amt * 0.20;
        bracketTax += t2Tax;
        remaining -= tier2Amt;
        breakdown.push({ label: "Tier 2 ($25MM-$50MM) @ 20%", val: fmt(t2Tax), note: `on ${fmtM(tier2Amt)}`, type: 'add' });

        // Tier 3: Next $25M ($50M-$75M) @ 25%
        let tier3Amt = Math.min(Math.max(0, remaining), 25000000);
        let t3Tax = tier3Amt * 0.25;
        bracketTax += t3Tax;
        remaining -= tier3Amt;
        breakdown.push({ label: "Tier 3 ($50MM-$75MM) @ 25%", val: fmt(t3Tax), note: `on ${fmtM(tier3Amt)}`, type: 'add' });

        // Tier 4: Next $75M ($75M-$150M) @ 30%
        let tier4Amt = Math.min(Math.max(0, remaining), 75000000);
        let t4Tax = tier4Amt * 0.30;
        bracketTax += t4Tax;
        remaining -= tier4Amt;
        breakdown.push({ label: "Tier 4 ($75MM-$150MM) @ 30%", val: fmt(t4Tax), note: `on ${fmtM(tier4Amt)}`, type: 'add' });

        // Tier 5: Next $450M ($150M-$600M) @ 35%
        let tier5Amt = Math.min(Math.max(0, remaining), 450000000);
        let t5Tax = tier5Amt * 0.35;
        bracketTax += t5Tax;
        remaining -= tier5Amt;
        breakdown.push({ label: "Tier 5 ($150MM-$600MM) @ 35%", val: fmt(t5Tax), note: `on ${fmtM(tier5Amt)}`, type: 'add' });

        // Tier 6: Over $600M @ 40%
        let t6Tax = Math.max(0, remaining) * 0.40;
        bracketTax += t6Tax;
        breakdown.push({ label: "Tier 6 (Over $600MM) @ 40%", val: fmt(t6Tax), note: `on ${fmtM(Math.max(0, remaining))}`, type: 'add' });

        let totalTax = supplementalTax + bracketTax;
        breakdown.push({ label: "TOTAL ESTIMATED TAX", val: fmt(totalTax), note: "", type: 'total' });

        // Effective Rate
        const effRate = agr > 0 ? (totalTax / agr) * 100 : 0;
        breakdown.push({ label: "Effective Tax Rate of AGR", val: effRate.toFixed(2) + '%', note: "", type: 'eff-rate' });

        // Step E: Total
        return { total: totalTax, breakdown: breakdown };
    }

    function calculateAGRFromTax(targetTax)
    {
        let low = 0;
        let high = 2000000000; // 2B
        let mid = 0;
        let iterations = 0;

        while (low <= high && iterations < 100)
        {
            mid = (low + high) / 2;
            const res = calculateTax(mid);
            const tax = res.total;

            if (Math.abs(tax - targetTax) < 100)
            { // Precision $100
                return mid;
            }

            if (tax < targetTax)
            {
                low = mid;
            } else
            {
                high = mid;
            }
            iterations++;
        }
        return mid;
    }

    function calculate(e)
    {
        if (isSyncing) return;

        const fmtInput = (v, dec = 0) => v.toLocaleString(undefined, { minimumFractionDigits: dec, maximumFractionDigits: dec });
        const isTextInput = e ? e.isTextInput : false;

        // 0. Check if location is selected
        const hasLocation = !!(lastImpactBreakdown && lastImpactBreakdown.countyFips);

        // Toggle Visibility for Net Impact Table
        const netImpactEmpty = document.getElementById('net-impact-empty-state');
        const netImpactContent = document.getElementById('net-impact-content');
        if (netImpactEmpty && netImpactContent)
        {
            if (hasLocation) { netImpactEmpty.classList.add('hidden'); netImpactContent.classList.remove('hidden'); }
            else { netImpactEmpty.classList.remove('hidden'); netImpactContent.classList.add('hidden'); }
        }

        // Toggle PDF Button State
        const pdfBtn = document.getElementById('btn-generate-pdf');
        if (pdfBtn)
        {
            const isSpinning = pdfBtn.querySelector('.animate-spin');
            if (!hasLocation)
            {
                pdfBtn.disabled = true;
            } else if (!isSpinning)
            {
                pdfBtn.disabled = false;
            }
        }

        // Toggle Visibility for Automated Analysis
        const analysisEmpty = document.getElementById('analysis-empty-state');
        const analysisContent = document.getElementById('analysis-content');
        if (analysisEmpty && analysisContent)
        {
            if (hasLocation) { analysisEmpty.classList.add('hidden'); analysisContent.classList.remove('hidden'); }
            else { analysisEmpty.classList.remove('hidden'); analysisContent.classList.add('hidden'); }
        }

        // Toggle Visibility for Detailed Social Cost Breakdown
        const breakdownEmpty = document.getElementById('detailed-breakdown-empty-state');
        const breakdownContent = document.getElementById('detailed-breakdown-content');

        // New calculation containers to toggle
        const taxRevenueContainer = document.getElementById('calc-tax-revenue-container');
        const taxAllocationContainer = document.getElementById('calc-tax-allocation-container');
        const problemGamblerContainer = document.getElementById('calc-problem-gambler-container');

        if (breakdownEmpty && breakdownContent)
        {
            if (hasLocation) 
            {
                breakdownEmpty.classList.add('hidden');
                breakdownContent.classList.remove('hidden');

                if (taxRevenueContainer) taxRevenueContainer.classList.remove('hidden');
                if (taxAllocationContainer) taxAllocationContainer.classList.remove('hidden');
                if (problemGamblerContainer) problemGamblerContainer.classList.remove('hidden');
            }
            else 
            {
                breakdownEmpty.classList.remove('hidden');
                breakdownContent.classList.add('hidden');

                if (taxRevenueContainer) taxRevenueContainer.classList.add('hidden');
                if (taxAllocationContainer) taxAllocationContainer.classList.add('hidden');
                if (problemGamblerContainer) problemGamblerContainer.classList.add('hidden');
            }
        }

        // Update Title
        const countyIndex = new Map(getCountyData().map(c => [String(c.geoid || c.id || ""), String(c.name || "").trim()]));
        const subjectCountyFips = String((lastImpactBreakdown && lastImpactBreakdown.countyFips) || "");
        const countyName = (lastImpactBreakdown && lastImpactBreakdown.countyName) || countyIndex.get(subjectCountyFips) || "Subject";
        const titleSuffix = hasLocation ? `${countyName} County ` : "";
        els.deficitTitle.innerHTML = `<span class="material-symbols-outlined text-red-500 align-middle mr-2">calculate</span> ${titleSuffix}Casino Net Economic Impact Analysis`;

        // 1. Two-Way Binding Logic (AGR vs Revenue)
        const source = e ? e.target : null;
        const sourceId = source ? source.id : "";
        let revenueM = 0;
        let agrM = 0;
        let taxResult = null;

        if (sourceId === 'input-agr')
        {
            // Master: AGR
            agrM = parseFloat(els.inAGR.value);
            taxResult = calculateTax(agrM * 1000000);
            revenueM = taxResult.total / 1000000;

            // Update Revenue (Dependent)
            els.inRevenue.value = revenueM.toFixed(2);
            if (els.valRevenue && document.activeElement !== els.valRevenue) els.valRevenue.value = revenueM.toFixed(2);
            // Update AGR (Self)
            if (els.valAGR && document.activeElement !== els.valAGR) els.valAGR.value = agrM.toFixed(2);

            try { isSyncing = true; els.inRevenue.dispatchEvent(new Event('input', { bubbles: true })); } finally { isSyncing = false; }
        } else if (sourceId === 'input-revenue')
        {
            // Master: Revenue
            revenueM = parseFloat(els.inRevenue.value);
            const targetTax = revenueM * 1000000;
            const agr = calculateAGRFromTax(targetTax);
            agrM = agr / 1000000;
            taxResult = calculateTax(agr); // Recalculate for breakdown

            // Update AGR (Dependent)
            els.inAGR.value = agrM.toFixed(2);
            if (els.valAGR && document.activeElement !== els.valAGR) els.valAGR.value = agrM.toFixed(2);
            // Update Revenue (Self)
            if (els.valRevenue && document.activeElement !== els.valRevenue) els.valRevenue.value = revenueM.toFixed(2);

            try { isSyncing = true; els.inAGR.dispatchEvent(new Event('input', { bubbles: true })); } finally { isSyncing = false; }
        } else
        {
            // Init or other inputs: Default to AGR as Master (Default 204.3M)
            agrM = parseFloat(els.inAGR.value);
            taxResult = calculateTax(agrM * 1000000);
            revenueM = taxResult.total / 1000000;

            if (!source)
            {
                els.inRevenue.value = revenueM.toFixed(2);
                if (els.valAGR && document.activeElement !== els.valAGR) els.valAGR.value = agrM.toFixed(2);
                if (els.valRevenue && document.activeElement !== els.valRevenue) els.valRevenue.value = revenueM.toFixed(2);
                try { isSyncing = true; els.inRevenue.dispatchEvent(new Event('input', { bubbles: true })); } finally { isSyncing = false; }
            }
        }

        // Update Displays - Handled by SliderInput component logic via 'input' event dispatch
        // els.valRevenue.textContent = '$' + revenueM.toFixed(1) + 'MM';
        // els.valAGR.textContent = '$' + agrM.toFixed(1) + 'MM';

        // RENDER TAX BREAKDOWN
        const container = document.getElementById('tax-details-container');
        if (container && taxResult && taxResult.breakdown)
        {
            // Force font-sans on container if not present
            container.classList.remove('font-mono');
            container.classList.add('font-sans');

            container.innerHTML = taxResult.breakdown.map(item =>
            {
                let colorClass = 'text-slate-400';
                if (item.type === 'total') colorClass = 'text-emerald-400 font-bold border-t border-slate-600 pt-2 mt-2';
                if (item.type === 'eff-rate') colorClass = 'text-emerald-600 dark:text-emerald-500 font-bold text-sm border-t border-slate-700/50 pt-1 mt-1';
                if (item.type === 'add') colorClass = 'text-emerald-500/80 pl-2';
                if (item.type === 'header') colorClass = 'text-white font-bold pb-1 border-b border-slate-700 mb-1';
                if (item.type === 'sub-header') colorClass = 'text-slate-200 font-semibold mt-2';
                if (item.type === 'info') colorClass = 'text-slate-500 italic pl-2';

                return `
                                                <div class="flex justify-between items-center ${colorClass}">
                                                    <div class="flex flex-col">
                                                        <span>${item.label}</span>
                                                        ${item.note ? `<span class="text-[10px] opacity-70 font-mono">${item.note}</span>` : ''}
                                                    </div>
                                                    <span class="font-mono">${item.val}</span>
                                                </div>
                                            `;
            }).join('');
        }

        // Snap Logic & Visual Update
        const inputs = [
            els.inRate, els.inAGR, els.inRevenue, els.inCostCrime, els.inCostBusiness,
            els.inCostBankruptcy, els.inCostIllness, els.inCostServices, els.inCostAbused
        ];

        inputs.forEach(input =>
        {
            if (!input) return;

            // Support multiple defaults for Rate slider
            const defs = input.id === 'input-rate' ? [2.3, 3.0, 5.5] : [parseFloat(input.dataset.default)];
            let val = parseFloat(input.value);
            const min = parseFloat(input.min);
            const max = parseFloat(input.max);
            const range = max - min;
            const threshold = range * 0.015;

            let snapped = false;
            let snapVal = null;

            for (const d of defs)
            {
                if (Math.abs(val - d) < threshold)
                {
                    snapped = true;
                    snapVal = d;
                    break;
                }
            }

            const valDisplayId = input.id.replace('input-', 'val-');
            const valDisplay = document.getElementById(valDisplayId);

            // Determine active color based on slider type
            let activeColor = null;
            // Rate slider uses default white text
            if (input.id === 'input-revenue') activeColor = 'text-emerald-500';
            else if (input.id === 'input-agr') activeColor = 'text-emerald-400';

            if (snapped) 
            {
                if (Math.abs(val - snapVal) > 0.001)
                {
                    input.value = snapVal;
                    val = snapVal;
                }

                if (valDisplay && activeColor)
                {
                    valDisplay.classList.remove('text-white');
                    valDisplay.classList.add(activeColor);
                }
            } else
            {
                if (valDisplay && activeColor)
                {
                    valDisplay.classList.remove(activeColor);
                    valDisplay.classList.add('text-white');
                }
            }

            // Update Sibling (Radio or Tick)
            if (input.id === 'input-rate')
            {
                const radios = document.querySelectorAll('input[name="gambling-rate-preset"]');
                radios.forEach(r => r.checked = (snapped && parseFloat(r.value) === snapVal));
            } else
            {
                const sibling = input.nextElementSibling;
                if (sibling && sibling.type === 'radio')
                {
                    sibling.checked = snapped;
                }
            }
        });

        // NOTE: revenueM is already calculated above
        const rate = parseFloat(els.inRate.value);

        // Get costs
        const cCrime = parseInt(els.inCostCrime.value);
        const cBusiness = parseInt(els.inCostBusiness.value);
        const cBankruptcy = parseInt(els.inCostBankruptcy.value);
        const cIllness = parseInt(els.inCostIllness.value);
        const cServices = parseInt(els.inCostServices.value);
        const cAbused = parseInt(els.inCostAbused.value);

        const costPer = cCrime + cBusiness + cBankruptcy + cIllness + cServices + cAbused;

        // Update value displays
        // els.valRevenue.textContent = '$' + revenueM.toFixed(1) + ' M'; // Already updated above
        // els.valRate.textContent = rate.toFixed(1) + '%';

        els.valCostTotal.textContent = '$' + costPer.toLocaleString();

        // Force update text inputs if they are not focused (fallback for SliderInputLogic)
        if (document.activeElement !== els.valCostCrime) els.valCostCrime.value = fmtInput(cCrime);
        if (document.activeElement !== els.valCostBusiness) els.valCostBusiness.value = fmtInput(cBusiness);
        if (document.activeElement !== els.valCostBankruptcy) els.valCostBankruptcy.value = fmtInput(cBankruptcy);
        if (document.activeElement !== els.valCostIllness) els.valCostIllness.value = fmtInput(cIllness);
        if (document.activeElement !== els.valCostServices) els.valCostServices.value = fmtInput(cServices);
        if (document.activeElement !== els.valCostAbused) els.valCostAbused.value = fmtInput(cAbused);

        // Logic
        const totalPopEl = document.getElementById('disp-pop-impact-zones');
        if (totalPopEl)
        {
            const val = parseInt(totalPopEl.textContent.replace(/,/g, ''));
            currentPop = !isNaN(val) && val > 0 ? val : 0;
        } else
        {
            currentPop = 0; // Fallback if no display element
        }

        // Retrieve Adult Population from Map DOM (populated by map.js)
        let adultPop = currentPop; // Fallback
        const adultPopEl = document.getElementById('disp-pop-adults');
        if (adultPopEl)
        {
            const val = parseInt(adultPopEl.textContent.replace(/,/g, ''));
            if (!isNaN(val) && val > 0) adultPop = val;
        }

        // Get victim count from the map's calculation.
        // PRIMARY: Use event data (lastImpactBreakdown.county.victims.total) - this is always accurate 
        // and includes baseline increase. The map dispatches this with the correct calculated value.
        // FALLBACK: Read from DOM only for initial load before any event has fired.

        let victims = 0;
        if (lastImpactBreakdown && lastImpactBreakdown.county && lastImpactBreakdown.county.victims && typeof lastImpactBreakdown.county.victims.total === 'number')
        {
            // Use the event data - this is the source of truth from the map's calculation
            victims = Math.round(lastImpactBreakdown.county.victims.total) || 0;
        }
        else
        {
            // Fallback to DOM read for initial load
            const tvEl = document.getElementById('total-gamblers');
            if (tvEl)
            {
                victims = parseInt(tvEl.textContent.replace(/,/g, '')) || 0;
            }
        }

        // If victims is 0 (initial load race condition), fallback to simple? 
        // No, map loads fast.

        // Calculate Effective Rate for display purposes (This is 'Problem Gambler Growth' rate, distinct from tax rate)
        // UPDATED: Use Adult Population as denominator per user request
        let gamblerGrowthRate = 0;
        if (adultPop > 0) gamblerGrowthRate = (victims / adultPop) * 100;

        // --- 5-GROUP COST CALCULATIONS ---
        // 1. Public Health (Humanitarian)
        const costHealthPer = cIllness;
        const totalCostHealth = victims * costHealthPer;

        // 2. Social Services (General)
        const costSocialPer = cServices;
        const totalCostSocial = victims * costSocialPer;

        // 3. Law Enforcement
        const costCrimePer = cCrime;
        const totalCostCrime = victims * costCrimePer;

        // 4. Civil Legal
        const costLegalPer = cBankruptcy;
        const totalCostLegal = victims * costLegalPer;

        // 5. Private Economy (Split)
        const costAbusedPer = cAbused;
        const totalCostAbused = victims * costAbusedPer;

        const costEmploymentPer = cBusiness;
        const totalCostEmployment = victims * costEmploymentPer;

        // Aggregate for Total Cost
        const totalCostEcon = totalCostAbused + totalCostEmployment; // Kept for backward compat with breakdown if needed
        const costEconPer = costAbusedPer + costEmploymentPer;

        const totalCost = totalCostHealth + totalCostSocial + totalCostCrime + totalCostLegal + totalCostEcon;
        const totalCostM = totalCost / 1000000;
        // --- REVENUE SPLIT ---
        const totalRevenue = revenueM * 1000000;

        // Get Allocation %
        const allocPct = parseInt(els.inAllocation.value);

        // Update Allocation Bar Visuals
        els.allocBarHuman.style.width = allocPct + '%';
        els.allocBarTaxpayer.style.width = (100 - allocPct) + '%';
        els.allocTextHuman.textContent = allocPct + '%';
        els.allocTextTaxpayer.textContent = (100 - allocPct) + '%';

        // Calculate Pools
        const revHealthPool = totalRevenue * (allocPct / 100);
        const revGeneralPool = totalRevenue * ((100 - allocPct) / 100);

        // --- COST COVERAGE LOGIC ---

        // 1. Humanitarian Fund covers Public Health first
        let revHealthAllocated = 0;
        let healthSurplus = 0;
        let healthUncovered = 0;

        if (revHealthPool >= totalCostHealth)
        {
            revHealthAllocated = totalCostHealth;
            healthSurplus = revHealthPool - totalCostHealth;
            healthUncovered = 0;
        } else
        {
            revHealthAllocated = revHealthPool;
            healthSurplus = 0;
            healthUncovered = totalCostHealth - revHealthPool;
        }

        // 2. General Fund covers remaining burdens (Taxpayer Health + Crime + Social + Legal)
        // We use a weighted average distribution for the General Fund
        // UPDATED: General Fund does NOT cover Public Health gaps. Public Health is solely the responsibility of the Humanitarian Fund.
        const burdenHealth = 0;
        const burdenCrime = totalCostCrime;
        const burdenSocial = totalCostSocial;
        const burdenLegal = totalCostLegal;

        const totalGeneralBurden = burdenHealth + burdenCrime + burdenSocial + burdenLegal;

        let revHealthFromGen = 0;
        let revCrime = 0;
        let revSocial = 0;
        let revLegal = 0;

        if (totalGeneralBurden > 0)
        {
            revHealthFromGen = revGeneralPool * (burdenHealth / totalGeneralBurden);
            revCrime = revGeneralPool * (burdenCrime / totalGeneralBurden);
            revSocial = revGeneralPool * (burdenSocial / totalGeneralBurden);
            revLegal = revGeneralPool * (burdenLegal / totalGeneralBurden);
        } else
        {
            // If no burden, revenue sits as surplus? 
            // For now, it remains unallocated in this logic block, 
            // but effectively it's a general surplus.
        }

        // Private gets 0
        const revEcon = 0;

        // --- BALANCES (NET IMPACT ANALYSIS) ---

        // Helpers
        const fmtM = (v) => '$' + (v / 1000000).toFixed(1) + 'MM';
        const fmt = (v) => '$' + v.toLocaleString(undefined, { maximumFractionDigits: 0 });
        const fmtDiff = (v) =>
        {
            return (v >= 0 ? '+$' : '-$') + Math.abs(v / 1000000).toFixed(1) + 'MM';
        };
        const setTxt = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };
        const setClass = (id, cls) => { const el = document.getElementById(id); if (el) el.className = cls; };

        // 1. Net Impact Analysis (Balance Sheet)
        const netHealthTax = revHealthFromGen - healthUncovered;

        const subHealthRev = revHealthPool + revHealthFromGen;
        const subHealthCost = revHealthAllocated + healthUncovered;
        const subHealthNet = healthSurplus + netHealthTax;

        const netCrime = revCrime - totalCostCrime;
        const netSocial = revSocial - totalCostSocial;
        const netLegal = revLegal - totalCostLegal;

        const subGenRev = revCrime + revSocial + revLegal;
        const subGenCost = totalCostCrime + totalCostSocial + totalCostLegal;
        const subGenNet = netCrime + netSocial + netLegal;

        const subPublicRev = subHealthRev + subGenRev;
        const subPublicCost = subHealthCost + subGenCost;
        const subPublicNet = subHealthNet + subGenNet;

        const totalCostPrivate = totalCostAbused + totalCostEmployment;
        const netTotalBalance = totalRevenue - totalCost;

        const subjectCountyName = (lastImpactBreakdown && lastImpactBreakdown.countyName) || countyIndex.get(subjectCountyFips) || subjectCountyFips || "Subject County";
        const subjectStateName = String((lastImpactBreakdown && lastImpactBreakdown.stateName) || "").trim();

        // Get the baseline increase from the slider
        const baselineIncreaseEl = document.getElementById('input-baseline-increase');
        const baselineIncrease = baselineIncreaseEl ? parseFloat(baselineIncreaseEl.value) || 0 : 0;

        const otherCosts = computeOtherCountyCosts({
            impactBreakdown: lastImpactBreakdown,
            baselineRate: rate,
            baselineIncrease: baselineIncrease,
            perVictimCosts: {
                health: costHealthPer,
                crime: costCrimePer,
                social: costSocialPer,
                legal: costLegalPer,
                abused: costAbusedPer,
                employment: costEmploymentPer
            }
        });

        // Store for PDF generation
        lastCalculationResult = {
            subjectCountyName: subjectCountyName,
            otherCosts: otherCosts
        };

        const o = otherCosts && otherCosts.totals ? otherCosts.totals : {};
        const otherTotals = {
            health: Number(o.health || 0),
            crime: Number(o.crime || 0),
            social: Number(o.social || 0),
            legal: Number(o.legal || 0),
            abused: Number(o.abused || 0),
            employment: Number(o.employment || 0),
            public: Number(o.public || 0),
            private: Number(o.private || 0),
            total: Number(o.total || 0)
        };
        const otherAdults = Math.round(Number(o.adults || 0));
        const otherVictims = Math.round(Number(o.victims || 0));
        const otherRate = otherAdults > 0 ? (otherVictims / otherAdults) * 100 : 0;
        const agrTotal = agrM * 1000000;
        const effectiveTaxRate = agrTotal > 0 ? (totalRevenue / agrTotal) * 100 : 0;

        const fmtVictims = Math.round(victims).toLocaleString();
        setTxt('calc-pop', Math.round(currentPop).toLocaleString());
        setTxt('calc-rate', gamblerGrowthRate.toFixed(2) + '%');
        setTxt('calc-result', fmtVictims);

        setTxt('calc-break-health-victims', fmtVictims);
        setTxt('calc-break-health-per', fmt(costHealthPer));
        setTxt('calc-break-health-total', fmtM(totalCostHealth));

        setTxt('calc-break-social-victims', fmtVictims);
        setTxt('calc-break-social-per', fmt(costSocialPer));
        setTxt('calc-break-social-total', fmtM(totalCostSocial));

        setTxt('calc-break-crime-victims', fmtVictims);
        setTxt('calc-break-crime-per', fmt(costCrimePer));
        setTxt('calc-break-crime-total', fmtM(totalCostCrime));

        setTxt('calc-break-legal-victims', fmtVictims);
        setTxt('calc-break-legal-per', fmt(costLegalPer));
        setTxt('calc-break-legal-total', fmtM(totalCostLegal));

        setTxt('calc-break-abused-victims', fmtVictims);
        setTxt('calc-break-abused-per', fmt(costAbusedPer));
        setTxt('calc-break-abused-total', fmtM(totalCostAbused));

        setTxt('calc-break-employment-victims', fmtVictims);
        setTxt('calc-break-employment-per', fmt(costEmploymentPer));
        setTxt('calc-break-employment-total', fmtM(totalCostEmployment));

        setTxt('calc-break-total-victims', fmtVictims);
        setTxt('calc-total-cost-per', fmt(costPer));
        setTxt('calc-total-cost-combined', fmtM(totalCost));

        const netImpactRows = [
            {
                key: 'health_human',
                kind: 'detail',
                label: 'Public Health (Humanitarian)',
                labelClass: '',
                tooltip: 'Addiction treatment, counseling, and prevention programs funded specifically by the Humanitarian Fund allocation.',
                revenue: revHealthPool,
                countyCost: revHealthAllocated,
                countyBalance: healthSurplus,
                otherCost: 0
            },
            {
                key: 'health_tax',
                kind: 'detail',
                label: 'Public Health (Taxpayer)',
                labelClass: '',
                tooltip: 'Excess public health costs falling on the general taxpayer after Humanitarian funds are exhausted.',
                revenue: revHealthFromGen,
                countyCost: healthUncovered,
                countyBalance: netHealthTax,
                otherCost: otherTotals.health
            },
            {
                key: 'health_sub',
                kind: 'subtotal',
                label: 'Subtotal: Public Health',
                labelClass: '',
                revenue: subHealthRev,
                countyCost: subHealthCost,
                countyBalance: subHealthNet,
                otherCost: otherTotals.health
            },
            {
                key: 'crime',
                kind: 'detail',
                label: 'Law Enforcement',
                labelClass: '',
                tooltip: 'Police response, investigations, and incarceration costs related to gambling-related crimes (theft, fraud, domestic disturbances).',
                revenue: revCrime,
                countyCost: totalCostCrime,
                countyBalance: netCrime,
                otherCost: otherTotals.crime
            },
            {
                key: 'social',
                kind: 'detail',
                label: 'Social Services',
                labelClass: '',
                tooltip: 'Unemployment benefits, welfare support, and child protective services for families destabilized by gambling addiction.',
                revenue: revSocial,
                countyCost: totalCostSocial,
                countyBalance: netSocial,
                otherCost: otherTotals.social
            },
            {
                key: 'legal',
                kind: 'detail',
                label: 'Civil Legal',
                labelClass: '',
                tooltip: 'Court costs for bankruptcy proceedings, divorce filings, and civil lawsuits associated with gambling debts.',
                revenue: revLegal,
                countyCost: totalCostLegal,
                countyBalance: netLegal,
                otherCost: otherTotals.legal
            },
            {
                key: 'gen_sub',
                kind: 'subtotal',
                label: 'Subtotal: General Taxpayer Services',
                labelClass: '',
                revenue: subGenRev,
                countyCost: subGenCost,
                countyBalance: subGenNet,
                otherCost: otherTotals.crime + otherTotals.social + otherTotals.legal
            },
            {
                key: 'public_sub',
                kind: 'subtotal',
                label: 'Subtotal: Public Sector Impact',
                labelClass: '',
                revenue: subPublicRev,
                countyCost: subPublicCost,
                countyBalance: subPublicNet,
                otherCost: otherTotals.public
            },
            {
                key: 'abused',
                kind: 'detail',
                label: 'Abused Dollars',
                labelClass: '',
                tooltip: 'Household spending diverted from local goods/services to gambling losses, reducing local consumer demand.',
                revenue: 0,
                countyCost: totalCostAbused,
                countyBalance: -totalCostAbused,
                otherCost: otherTotals.abused
            },
            {
                key: 'employment',
                kind: 'detail',
                label: 'Lost Employment',
                labelClass: '',
                tooltip: 'Productivity losses for local businesses due to employee absenteeism, distraction, and turnover related to gambling.',
                revenue: 0,
                countyCost: totalCostEmployment,
                countyBalance: -totalCostEmployment,
                otherCost: otherTotals.employment
            },
            {
                key: 'private_sub',
                kind: 'subtotal',
                label: 'Subtotal: Private Sector Impact',
                labelClass: '',
                revenue: 0,
                countyCost: totalCostPrivate,
                countyBalance: -totalCostPrivate,
                otherCost: otherTotals.private
            },
            {
                key: 'total',
                kind: 'total',
                label: 'Total Net Economic Impact',
                labelClass: 'text-white',
                revenue: totalRevenue,
                countyCost: totalCost,
                countyBalance: netTotalBalance,
                otherCost: otherTotals.total
            }
        ];

        const hasImpact = !!(lastImpactBreakdown && lastImpactBreakdown.countyFips);

        renderNetEconomicImpactTable({
            subjectCountyName,
            subjectCountyFips,
            subjectStateName,
            baselineRate: rate,
            rows: hasImpact ? netImpactRows : [],
            otherCounties: hasImpact && otherCosts ? otherCosts.counties : [],
            expanded: otherCountiesExpanded
        });

        if (hasImpact)
        {
            const taxEffRateActual = agrM > 0 ? (totalRevenue / (agrM * 1000000)) * 100 : 0;
            if (els.calcAGR) els.calcAGR.textContent = fmtM(agrM * 1000000);
            if (els.calcTaxRate) els.calcTaxRate.textContent = taxEffRateActual.toFixed(2) + '%';
            if (els.calcTaxTotal) els.calcTaxTotal.textContent = fmtM(totalRevenue);

            if (els.calcRevTotalH) els.calcRevTotalH.textContent = fmtM(totalRevenue);
            if (els.calcAllocPctH) els.calcAllocPctH.textContent = allocPct + '%';
            if (els.calcAllocHumanVal) els.calcAllocHumanVal.textContent = fmtM(revHealthPool);

            if (els.calcRevTotalG) els.calcRevTotalG.textContent = fmtM(totalRevenue);
            if (els.calcAllocPctG) els.calcAllocPctG.textContent = (100 - allocPct) + '%';
            if (els.calcAllocGenVal) els.calcAllocGenVal.textContent = fmtM(revGeneralPool);
        }

        const otherVictimsRaw = otherCosts && Array.isArray(otherCosts.counties)
            ? otherCosts.counties.reduce((sum, c) => sum + (Number(c && c.victimsWithin50) || 0), 0)
            : 0;
        const otherVictimsDisplay = hasImpact ? Math.round(otherVictimsRaw).toLocaleString() : '-';

        const otherPerDisplay = (v) => hasImpact ? fmt(v) : '-';
        const otherCostDisplay = (v) => hasImpact ? fmtM(v) : '-';

        setTxt('calc-break-health-victims-other', otherVictimsDisplay);
        setTxt('calc-break-health-per-other', otherPerDisplay(costHealthPer));
        setTxt('calc-break-health-total-other', otherCostDisplay(otherTotals.health));

        setTxt('calc-break-social-victims-other', otherVictimsDisplay);
        setTxt('calc-break-social-per-other', otherPerDisplay(costSocialPer));
        setTxt('calc-break-social-total-other', otherCostDisplay(otherTotals.social));

        setTxt('calc-break-crime-victims-other', otherVictimsDisplay);
        setTxt('calc-break-crime-per-other', otherPerDisplay(costCrimePer));
        setTxt('calc-break-crime-total-other', otherCostDisplay(otherTotals.crime));

        setTxt('calc-break-legal-victims-other', otherVictimsDisplay);
        setTxt('calc-break-legal-per-other', otherPerDisplay(costLegalPer));
        setTxt('calc-break-legal-total-other', otherCostDisplay(otherTotals.legal));

        setTxt('calc-break-abused-victims-other', otherVictimsDisplay);
        setTxt('calc-break-abused-per-other', otherPerDisplay(costAbusedPer));
        setTxt('calc-break-abused-total-other', otherCostDisplay(otherTotals.abused));

        setTxt('calc-break-employment-victims-other', otherVictimsDisplay);
        setTxt('calc-break-employment-per-other', otherPerDisplay(costEmploymentPer));
        setTxt('calc-break-employment-total-other', otherCostDisplay(otherTotals.employment));

        setTxt('calc-break-total-victims-other', otherVictimsDisplay);
        setTxt('calc-total-cost-per-other', otherPerDisplay(costPer));
        setTxt('calc-total-cost-combined-other', otherCostDisplay(otherTotals.total));

        // Bar
        let percentCovered = 0;
        if (totalCostM > 0)
        {
            percentCovered = Math.min(100, Math.max(0, (revenueM / totalCostM) * 100));
        } else
        {
            percentCovered = 100;
        }

        if (els.resBar) els.resBar.style.width = percentCovered + '%';

        if (els.resFooter)
        {
            if (percentCovered >= 100)
            {
                els.resFooter.innerHTML = `Revenue covers <strong class="text-white text-lg">100%</strong> of costs.`;
            } else
            {
                els.resFooter.innerHTML = `Tax Revenue covers only <strong class="text-white text-lg">${percentCovered.toFixed(0)}%</strong> of costs.`;
            }
        }

        // --- DYNAMIC ANALYSIS TEXT ---
        const analysisEl = document.getElementById('analysis-text');
        if (analysisEl)
        {
            const subjectCountyFips = String((lastImpactBreakdown && lastImpactBreakdown.countyFips) || (els.inCounty && els.inCounty.value) || "");
            const countyIndex = new Map(getCountyData().map(c => [String(c.geoid || c.id || ""), String(c.name || "").trim()]));
            const subjectCountyName = (lastImpactBreakdown && lastImpactBreakdown.countyName) || countyIndex.get(subjectCountyFips) || "Selected";
            const subjectStateName = String((lastImpactBreakdown && lastImpactBreakdown.stateName) || "Indiana").trim();

            const costRatio = totalRevenue > 0 ? (totalCost / totalRevenue).toFixed(2) : "N/A";
            const taxEffRate = agrM > 0 ? (totalRevenue / (agrM * 1000000)) * 100 : 0;

            const countyInfo = getCountyData().find(c => c.pop === currentPop) || { name: subjectCountyName, pop: currentPop };
            const baselineRateVal = parseFloat(els.inRate.value);

            function readInt(id)
            {
                const el = document.getElementById(id);
                if (!el) return 0;
                return parseInt(String(el.textContent || "").replace(/,/g, '')) || 0;
            }

            const countyBreakdown = (lastImpactBreakdown && lastImpactBreakdown.county) ? lastImpactBreakdown.county : null;
            const t1AdultsCounty = countyBreakdown && Number.isFinite(countyBreakdown.t1Adults) ? countyBreakdown.t1Adults : readInt('val-t1-county');
            const t2AdultsCounty = countyBreakdown && Number.isFinite(countyBreakdown.t2Adults) ? countyBreakdown.t2Adults : readInt('val-t2-county');
            const t3AdultsCounty = countyBreakdown && Number.isFinite(countyBreakdown.t3Adults) ? countyBreakdown.t3Adults : readInt('val-t3-county');

            const t1Pop = Math.round(t1AdultsCounty).toLocaleString();
            const t2Pop = Math.round(t2AdultsCounty).toLocaleString();
            const t3Pop = Math.round(t3AdultsCounty).toLocaleString();
            const t1Rate = document.getElementById('rate-t1') ? document.getElementById('rate-t1').textContent : "-%";
            const t2Rate = document.getElementById('rate-t2') ? document.getElementById('rate-t2').textContent : "-%";
            const t3Rate = document.getElementById('rate-t3') ? document.getElementById('rate-t3').textContent : "-%";
            const effRateDisplay = `${gamblerGrowthRate.toFixed(2)}%`;

            const regionalImpact = lastImpactBreakdown && lastImpactBreakdown.regional ? lastImpactBreakdown.regional : {};
            const regionalAdults = Number(regionalImpact.adultsWithin50 || 0);
            const otherCountiesCount = (otherCosts && Array.isArray(otherCosts.counties)) ? otherCosts.counties.length : 0;
            const otherTotalCost = Number(otherTotals.total || 0);
            const stateWideSocialCost = totalCost + otherTotalCost;
            const stateWideNetBalance = totalRevenue - stateWideSocialCost;

            let analysisHTML = '';

            // 1. Disclaimer (Sub-header + Bullet)
            analysisHTML += `<div class="font-bold text-white mb-2 uppercase tracking-wide text-sm underline">Disclaimers</div>`;
            analysisHTML += `<ul class="list-disc pl-8 space-y-1 mb-4 text-slate-300">`;
            analysisHTML += `<li><strong>Open-Source Transparency:</strong> In an effort to encourage community involvement and transparency, SaveFW has published the source code for this Economic Impact Calculator as free and open-source software (FOSS) under the AGPL-3.0 License, available on GitHub at <a href="https://github.com/tuckthomas/casino-economic-impact-calculator.git" target="_blank" class="underline text-blue-400 hover:text-blue-300 transition-colors">https://github.com/tuckthomas/casino-economic-impact-calculator.git</a>.</li>`;
            analysisHTML += `<li><strong>Substitution Effect:</strong> SaveFW's current analysis (v0.9.2 Beta) does not yet account for the <em>Substitution Effect</em>, also known as the displacement coefficient. This economic phenomenon describes how local casino spending displaces consumer expenditures that would otherwise circulate through existing local businesses—particularly in discretionary sectors such as retail, dining, and entertainment. Research indicates that for every $1,000 increase in casino revenue, businesses within a 10–30 mile radius may experience approximately $243 in lost sales (the "243 coefficient"). Because this displacement represents a net reduction in local economic activity, incorporating it would produce a more negative Net Economic Impact than currently reported.
                                            <ul class="list-[circle] pl-8 mt-2 space-y-3">
                                                <li><strong>Planned Methodology:</strong> Prior to SaveFW's 1.0.0 release, this analysis will implement a <em>Sector-Weighted Casino Displacement Model</em>. This enhanced approach will:
                                                    <ul class="list-[square] pl-8 mt-2 space-y-2">
                                                        <li>Apply the displacement coefficient only to local casino spending (excluding tourism/export dollars), using a configurable Local Share % slider;</li>
                                                        <li>Allocate displaced revenue across at-risk discretionary sectors (Retail NAICS 44–45, Dining/Hospitality NAICS 72, Entertainment NAICS 71) using either fixed baseline weights or data-driven weights based on local business inventory; and</li>
                                                        <li>Compute lost tax revenue using sector-specific assumptions for taxable sales share and profit margins derived from IRS Statistics of Income data.</li>
                                                    </ul>
                                                </li>
                                                <li><strong>Tax Waterfall:</strong> The model will calculate both lost sales tax (sector loss × taxability factor × sales tax rate) and lost income tax (sector loss × net income margin × effective income tax rate), providing a comprehensive picture of fiscal displacement.</li>
                                                <li><strong>Current Status:</strong> This enhancement is scheduled for implementation before the Casino Economic Net Impact Calculator reaches version 1.0.0. Users should be aware that current deficit projections may <em>understate</em> the true fiscal impact by not accounting for this substitution effect.</li>
                                            </ul>
                                        </li>`;
            analysisHTML += `<li><strong>Economic Multipliers:</strong> SaveFW's Economic Impact Calculator does not currently include positive economic multipliers often cited by proponents (the "Economic Engine" model). SaveFW is currently evaluating the methodological appropriateness of such multipliers in a "convenience casino" market context. This classification is corroborated by the State's Spectrum Study (2025), which indicates that for a Northeast Indiana location, approximately 95% of projected revenue is sourced from the primary catchment area (residents within a 90-minute radius). This metric suggests that the project primarily facilitates a reallocation of local capital to external corporate entities, rather than introducing new capital via external tourism. Given this context, SaveFW is assessing academic literature before making a determination regarding:
                                            <ul class="list-[circle] pl-8 mt-2 space-y-3">
                                                <li><strong>Leakage Rates:</strong> The extent to which revenue is retained locally versus exported to external corporate entities. Research from the <a href="https://www.civiceconomics.com/indie-impact.html" target="_blank" class="underline text-blue-400 hover:text-blue-300 transition-colors">Civic Economics "Indie Impact" Series</a> establishes that national corporate chains often return significantly less revenue to the local economy (approx. 14%) compared to independent local businesses (approx. 48%).</li>
                                                <li><strong>Multiplier Magnitudes:</strong> Determining accurate local multipliers for gaming revenues. While independent local businesses typically generate multipliers of <a href="https://www.civiceconomics.com/indie-impact.html" target="_blank" class="underline text-blue-400 hover:text-blue-300 transition-colors">~1.7–2.0</a> (recirculating ~$48–$53 per $100 spent), research from the <a href="https://massgaming.com/wp-content/uploads/Social-and-Economic-Impacts-of-Casino-Introduction-to-Massachusetts_Report.pdf" target="_blank" class="underline text-blue-400 hover:text-blue-300 transition-colors">Massachusetts Gaming Commission</a> indicates that regional "convenience" casinos may yield multipliers as low as 0.5–0.7 in scenarios where local retail cannibalization is factored into the economic model.</li>
                                                <li><strong>Net Fiscal Impact:</strong> The necessity of balancing indirect tax revenue against local expenditure substitution. Systematic reviews such as <a href="https://www.greo.ca/Modules/EvidenceCentre/Details/social-and-economic-impacts-gambling" target="_blank" class="underline text-blue-400 hover:text-blue-300 transition-colors">The Social and Economic Impacts of Gambling (2011)</a> and <a href="https://www.researchgate.net/publication/343114919_Does_Gambling_Harm_or_Benefit_Other_Industries_A_Systematic_Review" target="_blank" class="underline text-blue-400 hover:text-blue-300 transition-colors">"Does Gambling Harm or Benefit Other Industries? A Systematic Review"</a> conclude that while destination gambling can be beneficial, "convenience" gambling often substitutes for other local industries (retail and merchandise) rather than introducing new capital into the market.</li>
                                            </ul>
                                        </li>`;
            analysisHTML += `<li><strong>Programmatic Determinism:</strong> This tool provides a programmatic economic analysis based on deterministic, rule-based logic and peer-reviewed data, rather than an artificial intelligence (AI) model or probabilistic algorithm. Unlike AI, which can be unpredictable, this programmatic approach ensures that all results are derived from fixed, transparent formulas. Common usecases for deterministic analyses occur in highly regulated fields, such as banking. At a future date, SaveFW may include functionality to generate automated analyses utilizing more than the programmatic (deterministic) approach, as well as a blended approach which mitigates weaknesses of each. To provide clarity regarding the differences between the key methodologies in the pipeline, SaveFW has outlined the key distinctions below:
                                            <ul class="list-[circle] pl-8 mt-2 space-y-3">
                                                <li><strong>Programmatic (Deterministic) Analysis:</strong> This methodology operates on fixed-logic inputs and rigid mathematical formulas where the relationship between variables is constant. Given a specific set of parameters, the model generates an identical output in every iteration. It is used to produce exact, reproducible figures based on predefined causal relationships (e.g., $Revenue = P \times Q$).</li>
                                                <li><strong>Probabilistic (Stochastic) Analysis:</strong> This approach treats variables as probability distributions rather than static integers to account for market volatility. By employing random sampling and statistical techniques like Monte Carlo simulations, the model calculates a range of potential outcomes and the statistical likelihood of each, typically expressed as a confidence interval.</li>
                                                <li><strong>AI (Machine Learning) Analysis:</strong> This paradigm utilizes algorithms that identify non-linear correlations within high-dimensional datasets by developing internal weightings through training. Unlike rule-based systems, the logic is derived from statistical pattern recognition rather than human-coded formulas, allowing the model to adapt its predictive weightings as it processes new information.</li>
                                            </ul>
                                        </li>`;
            analysisHTML += `<li><strong>Model Responsibility:</strong> Individuals and entities should recognize that all economic models are projections subject to data limitations and inherent variability. The responsibility for any actions or decisions made based on this analysis rests solely with the individual or entity, and SaveFW assumes no liability for the application or interpretation of these results.</li>`;
            analysisHTML += `</ul>`;

            // 2. Assumptions (Sub-header + Bullets)
            analysisHTML += `<div class="font-bold text-white mb-2 uppercase tracking-wide text-sm underline">Assumptions</div>`;
            analysisHTML += `<ul class="list-disc pl-8 space-y-1 mb-4 text-slate-300">`;
            analysisHTML += `<li><strong>Prioritization:</strong> In the current iteration of this model, the Humanitarian Fund (${allocPct}%) is prioritized for Public Health expenditures. Future updates may include the functionality to define discrete allocations for various humanitarian initiatives.</li>`;
            analysisHTML += `<li><strong>Private Sector Burden:</strong> The Local Economy category represents private sector losses (productivity, debt) borne directly by households and businesses.</li>`;
            analysisHTML += `</ul>`;

            // Geographic Analysis
            analysisHTML += `<div class="font-bold text-white mb-2 uppercase tracking-wide text-sm underline">Geographic Analysis</div>`;
            analysisHTML += `<ul class="list-disc pl-8 space-y-1 mb-4 text-slate-300">`;
            analysisHTML += `<li><strong>Geospatial Data:</strong> Population data is sourced directly from the <a href="https://www.census.gov/data/developers/data-sets/decennial-census.html" target="_blank" class="underline text-blue-400 hover:text-blue-300 transition-colors">U.S. Census Bureau's 2020 Decennial Census API</a>, seeded into the SaveFW database in January 2026. Geographic boundaries utilize high-precision 2020 TIGER/Line Shapefiles processed via PostGIS.</li>`;
            analysisHTML += `<li><strong>Scope of Analysis:</strong> The primary balance-sheet results model fiscal exposure for the assessed county. The Net Economic Impact table also includes an Other Counties Costs column estimating how same-state impacts may distribute within a 50-mile radius (out-of-state excluded); use Expand to view the county-by-county breakout.</li>`;

            // updated specific bullet point with adult pop
            let adultPopStr = adultPop > 0 ? adultPop.toLocaleString() : "Unknown";
            analysisHTML += `<li><strong>Assessed Area:</strong> This analysis focuses on ${subjectCountyName} County, with a total population of ${countyInfo.pop.toLocaleString()}. The Adult Population (18+) for this area is ${adultPopStr}.</li>`;
            analysisHTML += `<li><strong>Regional Footprint:</strong> The model identifies ${otherCountiesCount} adjacent counties within the same state under the 50-mile impact radius, representing a total regional adult population of ${regionalAdults.toLocaleString()}.</li>`;

            analysisHTML += `<li><strong>Impact Distribution:</strong> This distribution models the increased statistical likelihood of residents developing problem gambling behaviors (Gambling Disorder) based on their geographic proximity to the casino site.
                                            <ul class="list-[circle] pl-8 mt-2 space-y-2">
                                                <li><strong>High Risk Zone (0-10 miles):</strong> ${t1Pop} county residents are subject to a ${t1Rate} prevalence rate due to immediate proximity.</li>
                                                <li><strong>Elevated Risk Zone (10-20 miles):</strong> ${t2Pop} county residents are subject to a ${t2Rate} prevalence rate.</li>`;

            if (Math.round(t3AdultsCounty) <= 0)
            {
                analysisHTML += `<li><strong>Baseline Risk Zone (20-50 miles):</strong> The county has effectively zero residents in the baseline band under the 50-mile model cutoff.</li>`;
            } else
            {
                analysisHTML += `<li><strong>Baseline Risk Zone (20-50 miles):</strong> ${t3Pop} county residents are subject to the baseline ${t3Rate} rate.</li>`;
            }
            analysisHTML += `</ul></li>`;
            // Updated conclusion to be clearer
            analysisHTML += `<li><strong>Prevalence Outcome:</strong> The resulting net effective problem gambler growth rate for the county is ${effRateDisplay} (of the adult population), projecting ${victims.toLocaleString()} new problem gamblers within the county (within 50 miles of the site). Regionally, an additional ${Math.round(otherVictims).toLocaleString()} new problem gamblers are projected in adjacent counties.</li>`;
            analysisHTML += `</ul>`;

            // 4. Analysis (Split into sections)

            // A. TAX REVENUE
            analysisHTML += `<div class="font-bold text-white mb-2 uppercase tracking-wide text-sm underline">Tax Revenue Analysis</div>`;
            analysisHTML += '<ul class="list-disc pl-8 space-y-3 mb-4 text-slate-300">';
            analysisHTML += `<li><strong>Adjusted Gross Revenue (AGR):</strong> The projected base of ${fmtM(agrM * 1000000)} represent the total wealth extracted from gamblers. Unlike standard sales or property taxes, gambling revenue is subject to a unique progressive structure in Indiana, utilizing a 3.5% supplemental tax and tiered brackets that scale based on volume.</li>`;
            analysisHTML += `<li><strong>Effective Tax Rate:</strong> For this scenario, the calculated effective tax rate is ${taxEffRate.toFixed(2)}%, resulting in ${fmtM(totalRevenue)} in estimated public tax revenue.</li>`;
            analysisHTML += `<li><strong>Revenue Allocation:</strong> The model directs ${allocPct}% (${fmtM(revHealthPool)}) to the Humanitarian Fund and ${100 - allocPct}% (${fmtM(revGeneralPool)}) to the General Fund. This General Fund portion is distributed pro rata (proportionally based on share of total cost) towards addressing remaining burdens: Law Enforcement, Social Services, and Civil Legal.</li>`;
            analysisHTML += '</ul>';

            // B. SOCIAL COSTS
            analysisHTML += `<div class="font-bold text-white mb-2 uppercase tracking-wide text-sm underline">Social Cost Analysis</div>`;
            analysisHTML += '<ul class="list-disc pl-8 space-y-3 mb-4 text-slate-300">';
            analysisHTML += `<li><strong>Data Source:</strong> Baseline social cost valuations are derived from peer-reviewed research by <a href="https://www.senate.ga.gov/committees/Documents/HiddenCostsofGam.pdf" target="_blank" class="underline text-blue-400 hover:text-blue-300 transition-colors">Grinols</a> (2011), with values adjusted for 2025 inflation to reflect current economic conditions.</li>`;
            // Public Health
            if (revHealthPool >= totalCostHealth)
            {
                analysisHTML += `<li><strong>Public Health:</strong> The ${allocPct}% Humanitarian allocation is fully sufficient to cover the projected ${fmtM(totalCostHealth)} in Public Health costs. This surplus enables proactive recovery initiatives, which can stabilize vulnerable households and reduce long-term homelessness.</li>`;
            } else
            {
                analysisHTML += `<li><strong>Public Health:</strong> The projected Public Health costs exceed the capacity of the ${allocPct}% Humanitarian allocation, leaving a funding gap for addiction and mental health treatments. This shortfall risks exacerbating homelessness and straining emergency medical services as untreated conditions escalate.</li>`;
            }
            // General Taxpayer Services Analysis (Dollar Amounts)

            // Law Enforcement
            const gapCrime = totalCostCrime - revCrime;
            if (gapCrime > 0)
            {
                analysisHTML += `<li><strong>Law Enforcement:</strong> The department faces a projected deficit of ${fmtM(gapCrime)}, necessitating a reduction in operational throughput. This budgetary deficit is projected to result in increased response latencies, diminished clearance rates for property crimes, and a reduced capacity to manage the anticipated surge in domestic disturbance calls.</li>`;
            } else
            {
                analysisHTML += `<li><strong>Law Enforcement:</strong> The department projects a budget surplus of ${fmtM(Math.abs(gapCrime))}, positioning it to maintain high operational readiness. This additional capacity enables the implementation of enhanced community policing initiatives and the establishment of specialized units dedicated to investigating complex financial crimes.</li>`;
            }

            // Social Services
            const gapSocial = totalCostSocial - revSocial;
            if (gapSocial > 0)
            {
                analysisHTML += `<li><strong>Social Services:</strong> This sector faces a projected deficit of ${fmtM(gapSocial)}, diminishing the programmatic efficacy of the regional social safety net. A deficit of this magnitude risks exceeding the throughput capacity of the foster care system and restricts the availability of essential support resources for households experiencing displacement or foreclosure.</li>`;
            } else
            {
                analysisHTML += `<li><strong>Social Services:</strong> With a projected surplus of ${fmtM(Math.abs(gapSocial))}, the county can maintain a robust and stable safety net. This financial stability facilitates the expansion of proactive family stabilization programs and ensures adequate resource availability for food banks and emergency housing assistance.</li>`;
            }

            // Courts (Civil Legal)
            const gapLegal = totalCostLegal - revLegal;
            if (gapLegal > 0)
            {
                analysisHTML += `<li><strong>Civil Legal (Courts):</strong> The judicial system faces a projected deficit of ${fmtM(gapLegal)}, resulting in systemic administrative bottlenecks. This budgetary deficit is anticipated to generate substantial backlogs in bankruptcy proceedings, extended processing delays for divorce and custody hearings, and an inability to manage the projected volume of eviction cases efficiently.</li>`;
            } else
            {
                analysisHTML += `<li><strong>Civil Legal (Courts):</strong> The system projects a budget surplus of ${fmtM(Math.abs(gapLegal))}, equipping it to maintain operational efficiency despite increased caseloads. This financial capacity facilitates the timely processing of civil matters and provides necessary funding for mediation services and diversion programs.</li>`;
            }

            // Private Sector (Local Economy) Breakout
            analysisHTML += `<li><strong>Abused Dollars:</strong> The private sector faces a projected unmitigated loss of ${fmtM(totalCostAbused)}. This represents direct wealth extraction from households where financial resources are diverted from essential needs and savings to service gambling debts, reducing overall local purchasing power.</li>`;

            analysisHTML += `<li><strong>Lost Employment:</strong> The local economy is projected to incur ${fmtM(totalCostEmployment)} in losses due to reduced workforce productivity. This includes costs associated with absenteeism, termination of problem gamblers, and the friction costs of rehiring and retraining, effectively operating as a hidden tax on local employers.</li>`;

            // Regional Spillover Summary
            analysisHTML += `<li><strong>Regional Spillover:</strong> Adjacent counties within ${subjectStateName} face a combined social cost liability of ${fmtM(otherTotalCost)}. These regional costs receive zero direct revenue offsets from the subject county's tax receipts, representing a net fiscal export of social burden.</li>`;

            analysisHTML += '</ul>';

            // C. NET ECONOMIC IMPACT
            analysisHTML += `<div class="font-bold text-white mb-2 uppercase tracking-wide text-sm underline">Net Economic Impact Analysis</div>`;
            analysisHTML += '<ul class="list-disc pl-8 space-y-3 text-slate-300">';

            analysisHTML += `<li><strong>Sector Comparison:</strong> The Public Sector (government departments) projected impact is a ${fmtM(Math.abs(subPublicNet))} ${subPublicNet >= 0 ? 'surplus' : 'deficit'}. In comparison, the Private Sector (Local Economy) faces an unmitigated loss of ${fmtM(totalCostPrivate)}.</li>`;

            analysisHTML += `<li><strong>Subject County Fiscal Balance:</strong> When balancing tax revenue against direct social costs, ${subjectCountyName} County realizes a net impact of ${fmtDiffM(netTotalBalance)}.</li>`;

            analysisHTML += `<li><strong>State-Wide Fiscal Balance:</strong> Considering the cumulative impact on ${subjectStateName} (the subject county plus regional spillover within 50 miles), the total net economic impact is ${fmtDiffM(stateWideNetBalance)}. This metric represents the comprehensive fiscal result for state taxpayers.</li>`;

            if (stateWideNetBalance < 0)
            {
                analysisHTML += `<li><strong>Fiscal Conclusion:</strong> Under this configuration, the project creates a net fiscal deficit for the state. The cumulative social costs (${fmtM(stateWideSocialCost)}) exceed the total tax revenue (${fmtM(totalRevenue)}), resulting in a net economic loss. To achieve a fiscal break-even point, proponents would need to demonstrate that unmodeled economic multipliers can generate at least ${fmtM(Math.abs(stateWideNetBalance))} in additional, non-substitutive value.</li>`;
                analysisHTML += `<li><strong>Cost-to-Benefit Ratio:</strong> For every $1 in tax revenue generated, the broader state economy is projected to incur $${(stateWideSocialCost / Math.max(1, totalRevenue)).toFixed(2)} in social costs (crime, bankruptcy, lost productivity).</li>`;
            } else
            {
                analysisHTML += `<li><strong>Fiscal Conclusion:</strong> Under this specific configuration of variables, the casino generates a net fiscal surplus for the state. The projected revenue of ${fmtM(totalRevenue)} exceeds the estimated combined social cost liabilities of ${fmtM(stateWideSocialCost)}.</li>`;
            }

            analysisHTML += '</ul>';
            analysisEl.innerHTML = analysisHTML;
        }
    }

    function computeOtherCountyCosts(options)
    {
        const impact = options && options.impactBreakdown ? options.impactBreakdown : null;
        const impacted = impact && Array.isArray(impact.byCounty) ? impact.byCounty : [];
        const subjectCountyFips = String((impact && impact.countyFips) || "");

        const baselineRateCandidate = Number((impact && impact.baselineRate) || (options && options.baselineRate) || 2.3);
        const baselineRate = Number.isFinite(baselineRateCandidate) ? baselineRateCandidate : 2.3;

        // Delta rates for NET NEW calculation (increase above baseline)
        // The baselineIncrease (Expected Baseline Increase slider) adds to ALL tiers
        // T1 (≤10mi): 2x baseline rate → delta = baseline + baselineIncrease
        // T2 (10-20mi): 1.5x baseline rate → delta = 0.5x baseline + baselineIncrease  
        // T3 (20-50mi): 1x baseline rate → delta = baselineIncrease only
        const baselineIncrease = Number(options.baselineIncrease || 0);
        const d1 = (baselineRate + baselineIncrease) / 100;        // proximity premium + baseline increase
        const d2 = ((baselineRate * 0.5) + baselineIncrease) / 100; // reduced proximity premium + baseline increase
        const d3 = baselineIncrease / 100;                          // baseline increase only (no proximity premium)

        const perVictimCosts = (options && options.perVictimCosts) ? options.perVictimCosts : {};
        const costsPerVictim = {
            health: Number(perVictimCosts.health || 0),
            crime: Number(perVictimCosts.crime || 0),
            social: Number(perVictimCosts.social || 0),
            legal: Number(perVictimCosts.legal || 0),
            abused: Number(perVictimCosts.abused || 0),
            employment: Number(perVictimCosts.employment || 0)
        };

        const countyIndex = new Map(getCountyData().map(c => [String(c.geoid || c.id || ""), String(c.name || "").trim()]));

        const totals = {
            adults: 0,
            victims: 0,
            health: 0,
            crime: 0,
            social: 0,
            legal: 0,
            abused: 0,
            employment: 0,
            public: 0,
            private: 0,
            total: 0
        };

        const counties = [];
        for (const c of impacted)
        {
            const fips = String((c && (c.fips || c.geoid)) || "");
            if (!fips || fips === subjectCountyFips) continue;

            const t1Adults = Number(c.t1Pop || 0);
            const t2Adults = Number(c.t2Pop || 0);
            const t3Adults = Number(c.t3Pop || 0);
            const adultsWithin50 = t1Adults + t2Adults + t3Adults;
            const victimsWithin50 = (t1Adults * d1) + (t2Adults * d2) + (t3Adults * d3);
            if (!Number.isFinite(victimsWithin50) || victimsWithin50 <= 0) continue;

            // Use name from byCountyArray if provided, otherwise lookup, fallback to FIPS
            const name = c.name || countyIndex.get(fips) || fips;

            const health = victimsWithin50 * costsPerVictim.health;
            const crime = victimsWithin50 * costsPerVictim.crime;
            const social = victimsWithin50 * costsPerVictim.social;
            const legal = victimsWithin50 * costsPerVictim.legal;
            const abused = victimsWithin50 * costsPerVictim.abused;
            const employment = victimsWithin50 * costsPerVictim.employment;
            const publicTotal = health + crime + social + legal;
            const privateTotal = abused + employment;
            const total = publicTotal + privateTotal;

            counties.push({
                fips,
                name,
                adultsWithin50,
                victimsWithin50,
                costs: { health, crime, social, legal, abused, employment, public: publicTotal, private: privateTotal, total }
            });

            totals.health += health;
            totals.crime += crime;
            totals.social += social;
            totals.legal += legal;
            totals.abused += abused;
            totals.employment += employment;
            totals.adults += adultsWithin50;
            totals.victims += victimsWithin50;
        }

        totals.public = totals.health + totals.crime + totals.social + totals.legal;
        totals.private = totals.abused + totals.employment;
        totals.total = totals.public + totals.private;

        counties.sort((a, b) => (b.costs.total || 0) - (a.costs.total || 0));

        return { counties, totals, baselineRate };
    }

    function getOtherCountyCostForRow(rowKey, costs)
    {
        const c = costs || {};
        switch (rowKey)
        {
            case 'health_human': return 0;
            case 'health_tax': return Number(c.health || 0);
            case 'health_sub': return Number(c.health || 0);
            case 'crime': return Number(c.crime || 0);
            case 'social': return Number(c.social || 0);
            case 'legal': return Number(c.legal || 0);
            case 'gen_sub': return Number((c.crime || 0) + (c.social || 0) + (c.legal || 0));
            case 'public_sub': return Number(c.public || 0);
            case 'abused': return Number(c.abused || 0);
            case 'employment': return Number(c.employment || 0);
            case 'private_sub': return Number(c.private || 0);
            case 'total': return Number(c.total || 0);
            default: return 0;
        }
    }

    window.renderNetEconomicImpactTable = renderNetEconomicImpactTable;
    function renderNetEconomicImpactTable(model)
    {
        const container = document.getElementById('net-impact-table');
        if (!container) return;

        const noteEl = document.getElementById('net-impact-note');

        const rows = (model && Array.isArray(model.rows)) ? model.rows : [];
        const otherCounties = (model && Array.isArray(model.otherCounties)) ? model.otherCounties : [];
        const expanded = !!(model && model.expanded && otherCounties.length > 0);
        const baselineRate = Number(model && model.baselineRate);
        const baselineRateDisplay = Number.isFinite(baselineRate) ? baselineRate.toFixed(1) : "—";

        const subjectCountyName = String((model && model.subjectCountyName) || "").trim();
        const subjectStateName = String((model && model.subjectStateName) || "").trim();
        const countyRevenueHeaderText = subjectCountyName
            ? (/\bcounty\b/i.test(subjectCountyName) ? `${subjectCountyName} Revenue` : `${subjectCountyName} County Revenue`)
            : 'County Revenue';
        const countyHeaderText = subjectCountyName
            ? (/\bcounty\b/i.test(subjectCountyName) ? `${subjectCountyName} Costs` : `${subjectCountyName} County Costs`)
            : 'County Costs';
        const countyNetHeaderText = subjectCountyName
            ? (/\bcounty\b/i.test(subjectCountyName) ? `${subjectCountyName} Net Balance` : `${subjectCountyName} County Net Balance`)
            : 'County Net Balance';
        const stateNetHeaderText = subjectStateName ? `${subjectStateName} Net Balance` : 'Total Net Balance';
        const otherHeaderText = otherCounties.length
            ? `Other ${subjectStateName || 'State'} Counties Cost\u00a0(${otherCounties.length})`
            : `Other ${subjectStateName || 'State'} Counties Cost`;

        if (!rows.length)
        {
            container.innerHTML = `<div class="p-4 text-sm text-slate-500 italic text-center">Select a county on the map to see cost distribution.</div>`;
            if (noteEl) noteEl.textContent = "";
            if (otherCountiesToggleEl) otherCountiesToggleEl.remove();
            otherCountiesToggleEl = null;
            otherCountiesToggleState = null;
            return;
        }

        const headerExtra = expanded
            ? otherCounties.map(c => `<th class="px-3 py-2 text-right text-slate-200 max-w-[120px]">${escapeHtml(c.name)}</th>`).join('')
            : '';

        const thead = `
			            <thead>
			                <tr class="border-b border-slate-700 bg-slate-900/60 text-slate-200">
			                    <th class="px-3 py-2 text-left sticky left-0 bg-slate-950/90 backdrop-blur max-w-[100px] border-r border-slate-800/80" data-col="group">GROUP</th>
			                    <th class="px-3 py-2 text-right max-w-[150px] border-r border-slate-800/80">${escapeHtml(countyRevenueHeaderText.toUpperCase())}</th>
			                    <th class="px-3 py-2 text-right max-w-[150px] border-r border-slate-800/80">${escapeHtml(countyHeaderText.toUpperCase())}</th>
			                    <th class="px-3 py-2 text-right max-w-[150px] border-r border-slate-800/80">${escapeHtml(countyNetHeaderText.toUpperCase())}</th>
			                    <th class="px-3 py-2 text-right max-w-[180px] border-r border-slate-700 relative group" data-col="other-cost">${otherHeaderText.toUpperCase()}</th>
			                    ${headerExtra}
			                    <th class="px-3 py-2 text-right sticky right-0 bg-slate-950/90 backdrop-blur max-w-[150px]">${escapeHtml(stateNetHeaderText.toUpperCase())}</th>
			                </tr>
			            </thead>
			        `;

        let tbody = '<tbody>';
        for (const row of rows)
        {
            const kind = String(row.kind || 'detail');
            const rowKey = String(row.key || "");

            const rowRevenue = Number(row.revenue || 0);
            const rowCountyCost = Number(row.countyCost || 0);
            const rowOtherCost = Number(row.otherCost || 0);
            const rowCountyBalance = Number(row.countyBalance || (rowRevenue - rowCountyCost));
            const rowTotalBalance = rowCountyBalance - rowOtherCost;

            const revenueClass = rowRevenue > 0 ? 'text-emerald-400' : (rowRevenue < 0 ? 'text-red-500' : 'text-slate-200');
            const countyCostClass = rowCountyCost !== 0 ? 'text-red-400' : 'text-slate-200';
            const countyBalanceClass = rowCountyBalance > 0 ? 'text-emerald-400' : (rowCountyBalance < 0 ? 'text-red-500' : 'text-slate-200');
            const otherCostClass = rowOtherCost !== 0 ? 'text-red-400' : 'text-slate-200';
            const totalBalanceClass = rowTotalBalance > 0 ? 'text-emerald-400' : (rowTotalBalance < 0 ? 'text-red-500' : 'text-slate-200');

            const fmtVal = (val, fmtFn) => (Math.abs(val) < 0.01) ? '-' : fmtFn(val);

            const rowBgClass = kind === 'total'
                ? 'bg-slate-800/60 border-t-2 border-slate-500'
                : kind === 'subtotal'
                    ? 'bg-slate-800/30 border-t border-slate-600'
                    : 'border-t border-slate-800/60';

            const rowFontClass = kind === 'total'
                ? 'font-black'
                : kind === 'subtotal'
                    ? 'font-bold'
                    : 'font-semibold';

            const tooltip = row.tooltip
                ? `
		                    <div class="flex items-center">
		                        <span class="material-symbols-outlined text-slate-400 text-[14px] cursor-help hover:text-slate-200 transition-colors" 
                                      onmouseenter="window.EconomicCalculator && window.EconomicCalculator.showTooltip(event, '${escapeHtml(row.tooltip).replace(/'/g, "\\'")}')" 
                                      onmouseleave="window.EconomicCalculator && window.EconomicCalculator.hideTooltip()"
                                      onmousemove="window.EconomicCalculator && window.EconomicCalculator.moveTooltip(event)">info</span>
		                    </div>
		                `
                : '';

            const labelHtml = (kind === 'total' || kind === 'subtotal')
                ? `<div class="uppercase tracking-wider ${kind === 'subtotal' ? 'pl-4' : ''}">${escapeHtml(row.label || '')}</div>`
                : escapeHtml(row.label || '');

            const labelCell = `
		                <td class="px-3 py-2 whitespace-nowrap sticky left-0 bg-slate-950/90 backdrop-blur text-slate-200 ${rowFontClass} ${row.labelClass || ''} border-r border-slate-800/80">
		                    <div class="flex items-center gap-1">
		                        <span>${labelHtml}</span>
		                        ${tooltip}
		                    </div>
		                </td>
		            `;

            const otherExtraCells = expanded
                ? otherCounties.map(c =>
                {
                    const val = getOtherCountyCostForRow(rowKey, c.costs);
                    const cellClass = val !== 0 ? 'text-red-400' : 'text-slate-200';
                    return `<td class="px-3 py-2 text-right font-mono whitespace-nowrap ${cellClass}">${fmtVal(val, fmtM)}</td>`;
                }).join('')
                : '';

            tbody += `
			                <tr class="${rowBgClass}">
			                    ${labelCell}
			                    <td class="px-3 py-2 text-right font-mono whitespace-nowrap ${revenueClass} border-r border-slate-800/80">${fmtVal(rowRevenue, fmtM)}</td>
			                    <td class="px-3 py-2 text-right font-mono whitespace-nowrap ${countyCostClass} border-r border-slate-800/80">${fmtVal(rowCountyCost, fmtM)}</td>
			                    <td class="px-3 py-2 text-right font-mono whitespace-nowrap ${rowFontClass} ${countyBalanceClass} border-r border-slate-800/80">${fmtVal(rowCountyBalance, fmtDiffM)}</td>
			                    <td class="px-3 py-2 text-right font-mono whitespace-nowrap ${otherCostClass} border-r border-slate-700">${fmtVal(rowOtherCost, fmtM)}</td>
			                    ${otherExtraCells}
			                    <td class="px-3 py-2 text-right font-mono whitespace-nowrap sticky right-0 bg-slate-950/95 backdrop-blur ${rowFontClass} ${totalBalanceClass}">${fmtVal(rowTotalBalance, fmtDiffM)}</td>
			                </tr>
			            `;
        }
        tbody += '</tbody>';

        container.innerHTML = `
		            <table class="w-full text-xs">
		                ${thead}
		                ${tbody}
		            </table>
		        `;

        otherCountiesToggleState = { container, count: otherCounties.length, expanded };
        positionOtherCountiesToggle();
        if (!otherCountiesToggleResizeBound)
        {
            const reposition = () => { positionOtherCountiesToggle(); };
            window.addEventListener('resize', reposition);
            const scrollParent = container.closest('.overflow-x-auto');
            if (scrollParent) scrollParent.addEventListener('scroll', reposition);
            window.addEventListener('scroll', reposition, true);
            otherCountiesToggleResizeBound = true;
        }

        if (noteEl)
        {
            noteEl.textContent = `Baseline rate: ${baselineRateDisplay}%. ${countyNetHeaderText} = County Revenue − County Costs. ${stateNetHeaderText} = County Revenue − (County Costs + Other Counties Costs). Other Counties Costs totals same-state spillover within 50 miles (no revenue offsets modeled for those counties).`;
        }
    }

    function initListeners()
    {
        const inputs = [
            els.inRevenue, els.inAGR, els.inAllocation,
            els.inCostCrime, els.inCostBusiness, els.inCostBankruptcy,
            els.inCostIllness, els.inCostServices, els.inCostAbused,
            els.inRate
        ];

        inputs.forEach(input =>
        {
            if (input) input.addEventListener('input', calculate);
        });

        // Listen for map updates (replaces old county select logic)
        window.addEventListener('impact-breakdown-updated', (e) =>
        {
            if (e.detail)
            {
                console.log('[Calculator] Received impact-breakdown-updated event, victims:', e.detail.county?.victims?.total);
                lastImpactBreakdown = e.detail;

                // Update local state if needed
                if (lastImpactBreakdown.county && lastImpactBreakdown.county.total)
                {
                    currentPop = lastImpactBreakdown.county.total;
                }

                calculate();
            }
        });

        // Global listener for custom events from SliderInput
        window.addEventListener('slider-input-sync', (e) =>
        {
            calculate(e);
        });
    }

    // Main init function - called by Blazor after component renders
    function init()
    {
        if (isInitialized && els.inAGR && document.body.contains(els.inAGR))
        {
            console.log('EconomicCalculator already initialized, skipping');
            return;
        }

        console.log('Initializing EconomicCalculator...');

        // Reset stale data from previous navigation
        lastImpactBreakdown = null;
        lastCalculationResult = null;
        currentPop = 0;

        // Populate DOM references
        initElements();

        // Set up event listeners
        initListeners();

        // Run initial calculation
        calculate();

        isInitialized = true;
        console.log('EconomicCalculator initialized successfully');
    }

    // Return public API
    function updateCounties()
    {
        if (!els.inCounty) initElements();
        initCounties();
        renderCustomOptions(getCountyData());
    }

    function toggleOtherCounties()
    {
        hideTooltip();
        otherCountiesExpanded = !otherCountiesExpanded;
        calculate();
    }

    let globalTooltip = null;
    let otherCountiesToggleEl = null;
    let otherCountiesToggleState = null;
    let otherCountiesToggleResizeBound = false;
    function ensureGlobalTooltip()
    {
        if (globalTooltip) return;
        globalTooltip = document.createElement('div');
        globalTooltip.id = 'economic-calculator-global-tooltip';
        globalTooltip.className = 'fixed hidden pointer-events-none z-[10000] w-64 p-3 bg-slate-900 text-white text-xs rounded-lg shadow-2xl border border-slate-700 font-normal whitespace-normal transition-opacity duration-200 opacity-0';
        document.body.appendChild(globalTooltip);
        // Hide tooltip on any scroll event to prevent sticking
        window.addEventListener('scroll', () => hideTooltip(), true);
    }

    function showTooltip(e, text)
    {
        ensureGlobalTooltip();
        globalTooltip.textContent = text;
        globalTooltip.classList.remove('hidden');
        // Force reflow
        void globalTooltip.offsetWidth;
        globalTooltip.classList.add('opacity-100');
        moveTooltip(e);
    }

    function hideTooltip()
    {
        if (!globalTooltip) return;
        globalTooltip.classList.remove('opacity-100');
        globalTooltip.classList.add('opacity-0');
        setTimeout(() => { if (globalTooltip.classList.contains('opacity-0')) globalTooltip.classList.add('hidden'); }, 200);
    }

    function moveTooltip(e)
    {
        if (!globalTooltip) return;
        const x = e.clientX;
        const y = e.clientY;
        const width = globalTooltip.offsetWidth;
        const height = globalTooltip.offsetHeight;
        const padding = 15;

        let left = x + padding;
        let top = y - height - padding;

        // Boundary check
        if (left + width > window.innerWidth) left = x - width - padding;
        if (top < 0) top = y + padding;

        globalTooltip.style.left = `${left}px`;
        globalTooltip.style.top = `${top}px`;
    }

    function positionOtherCountiesToggle()
    {
        if (!otherCountiesToggleState) return;

        const { container, count, expanded } = otherCountiesToggleState;
        const table = container.querySelector('table');
        const parent = document.body;
        if (!parent || !table)
        {
            if (otherCountiesToggleEl) otherCountiesToggleEl.style.display = 'none';
            return;
        }

        if (!count)
        {
            if (otherCountiesToggleEl) otherCountiesToggleEl.remove();
            otherCountiesToggleEl = null;
            return;
        }

        const headerCell = table.querySelector('th[data-col="other-cost"]');
        const groupCell = table.querySelector('th[data-col="group"]');
        if (!headerCell)
        {
            if (otherCountiesToggleEl) otherCountiesToggleEl.style.display = 'none';
            return;
        }

        if (!otherCountiesToggleEl)
        {
            otherCountiesToggleEl = document.createElement('div');
            otherCountiesToggleEl.className = 'other-counties-toggle group';
            otherCountiesToggleEl.style.position = 'fixed';
            otherCountiesToggleEl.style.top = '0';
            otherCountiesToggleEl.style.width = '0';
            otherCountiesToggleEl.style.pointerEvents = 'auto';
            parent.appendChild(otherCountiesToggleEl);
        }

        const headerRect = headerCell.getBoundingClientRect();
        const tableRect = table.getBoundingClientRect();
        const parentRect = (container.closest('.overflow-x-auto') || container).getBoundingClientRect();

        let targetLeft = headerRect.right;
        if (headerRect.right > parentRect.right && groupCell)
        {
            const groupRect = groupCell.getBoundingClientRect();
            targetLeft = groupRect.right;
        }
        const clampedLeft = Math.min(Math.max(targetLeft, parentRect.left), parentRect.right);
        const top = Math.max(tableRect.top, parentRect.top);
        const bottom = Math.min(tableRect.bottom, parentRect.bottom);
        const height = Math.max(0, bottom - top);

        otherCountiesToggleEl.style.display = 'block';
        otherCountiesToggleEl.style.left = `${Math.round(clampedLeft)}px`;
        otherCountiesToggleEl.style.top = `${top}px`;
        otherCountiesToggleEl.style.height = `${Math.round(height)}px`;
        otherCountiesToggleEl.style.zIndex = '2000';

        const label = expanded ? 'hide' : 'view';
        const lineClass = `absolute left-0 top-0 bottom-0 w-px ${expanded ? 'bg-slate-600' : 'bg-slate-700'} transition-colors ${expanded ? '' : 'group-hover:bg-slate-300'}`;
        const buttonClass = 'absolute left-0 top-1/2 -translate-y-1/2 -translate-x-1/2 w-7 h-7 rounded-full border border-slate-500 bg-slate-900 text-slate-200 hover:text-white hover:border-slate-300 hover:bg-slate-700 shadow-[0_4px_0_rgba(0,0,0,0.35)] transition-colors text-sm font-bold leading-none flex items-center justify-center pointer-events-auto px-1 z-10';
        const activePress = "this.style.transform='translate(-50%, -45%)'; this.style.boxShadow='0 1px 0 rgba(0,0,0,0.35)';";
        const activeRelease = "this.style.transform='translate(-50%, -50%)'; this.style.boxShadow='0 4px 0 rgba(0,0,0,0.35)';";
        const hideTooltip = "window.EconomicCalculator && window.EconomicCalculator.hideTooltip && window.EconomicCalculator.hideTooltip();";

        otherCountiesToggleEl.innerHTML = `
            <span class="${lineClass}"></span>
            <button type="button"
                onclick="window.EconomicCalculator && window.EconomicCalculator.toggleOtherCounties && window.EconomicCalculator.toggleOtherCounties()"
                onmouseenter="window.EconomicCalculator && window.EconomicCalculator.showTooltip && window.EconomicCalculator.showTooltip(event, 'Click to ${label} each of the ${count} counties costs.')"
                onmouseleave="${hideTooltip} ${activeRelease}"
                onmousemove="window.EconomicCalculator && window.EconomicCalculator.moveTooltip && window.EconomicCalculator.moveTooltip(event)"
                onmousedown="${activePress}"
                onmouseup="${activeRelease}"
                onmouseleave="${activeRelease}"
                style="transform: translate(-50%, -50%);"
                class="${buttonClass}">${expanded ? '−' : '+'}</button>
        `;
    }

    return {
        init: init,
        calculate: calculate,
        selectCounty: selectCounty,
        updateCounties: updateCounties,
        toggleOtherCounties: toggleOtherCounties,
        showTooltip: showTooltip,
        hideTooltip: hideTooltip,
        moveTooltip: moveTooltip,
        getLastCalculationData: () => lastCalculationResult
    };
})();

const benchmarkAgrPresets = {
    steubenLow: 188.6,
    steubenBase: 203.1,
    steubenHigh: 214.0
};

function setActiveAgrPresetButton(presetKey)
{
    const buttons = document.querySelectorAll('[data-agr-preset]');
    buttons.forEach(button =>
    {
        const isActive = presetKey && button.dataset.agrPreset === presetKey;
        button.classList.toggle('border-emerald-400', !!isActive);
        button.classList.toggle('bg-emerald-700/30', !!isActive);
        button.classList.toggle('text-emerald-200', !!isActive);
        button.classList.toggle('border-slate-600', !isActive);
        button.classList.toggle('text-white', !isActive);
    });
}

// Global function for applying AGR presets directly (AGR/GGR in MM).
window.applyAgrPreset = function(agrValue, label = null, presetKey = null)
{
    const parsedAgr = parseFloat(agrValue);
    if (!Number.isFinite(parsedAgr) || parsedAgr < 0) return;

    const normalizedAgr = parsedAgr.toFixed(2);
    const agrSlider = document.getElementById('slider-agr');
    const agrInput = document.getElementById('input-agr');

    if (agrSlider)
    {
        agrSlider.value = normalizedAgr;
    }

    if (agrInput)
    {
        agrInput.value = normalizedAgr;
        agrInput.dispatchEvent(new Event('input', { bubbles: true }));
        agrInput.dispatchEvent(new Event('change', { bubbles: true }));
    }

    if (agrSlider)
    {
        agrSlider.dispatchEvent(new Event('input', { bubbles: true }));
        agrSlider.dispatchEvent(new Event('change', { bubbles: true }));
    }

    setActiveAgrPresetButton(presetKey);
    window.currentAgrPreset = label || null;
};

// Backward-compatible multiplier helper for any existing callers.
window.applyAgrSensitivity = function(multiplier)
{
    const baseAgr = 204.3;
    const numericMultiplier = parseFloat(multiplier);
    if (!Number.isFinite(numericMultiplier)) return;
    const newAgr = baseAgr * numericMultiplier;
    window.applyAgrPreset(newAgr, `Sensitivity (${numericMultiplier})`);
};

window.benchmarkAgrPresets = benchmarkAgrPresets;

// Expose for Blazor
window.initEconomicCalculator = window.EconomicCalculator.init;

/* --- Simulator Modal Extensions --- */
window.currentSimStep = 1;

window.openSimulatorModal = function ()
{
    const modal = document.getElementById('simulator-modal');
    if (!modal) return;
    modal.classList.remove('hidden');
    // Force reflow
    void modal.offsetWidth;
    modal.classList.remove('opacity-0');
    window.currentSimStep = 1;
    window.updateSimStep();
};

window.closeSimulatorModal = function ()
{
    const modal = document.getElementById('simulator-modal');
    if (!modal) return;
    modal.classList.add('opacity-0');
    setTimeout(() =>
    {
        modal.classList.add('hidden');
    }, 300);
};

window.goToSimStep = function (step)
{
    window.currentSimStep = step;
    window.updateSimStep();
};

window.nextSimStep = function ()
{
    if (window.currentSimStep < 4)
    {
        window.currentSimStep++;
        window.updateSimStep();
    }
};

window.prevSimStep = function ()
{
    if (window.currentSimStep > 1)
    {
        window.currentSimStep--;
        window.updateSimStep();
    }
};

window.updateSimStep = function ()
{
    // Content Visibility
    for (let i = 1; i <= 4; i++)
    {
        const el = document.getElementById(`sim-step-${i}`);
        if (el)
        {
            if (i === window.currentSimStep) el.classList.remove('hidden');
            else el.classList.add('hidden');
        }
    }

    // Buttons
    const btnBack = document.getElementById('sim-btn-back');
    const btnCancel = document.getElementById('sim-btn-cancel');
    const btnNext = document.getElementById('sim-btn-next');
    const btnRun = document.getElementById('sim-btn-run');

    if (window.currentSimStep === 1)
    {
        if (btnBack) btnBack.classList.add('hidden');
        if (btnCancel) btnCancel.classList.remove('hidden');
        if (btnNext) btnNext.classList.remove('hidden');
        if (btnRun) btnRun.classList.add('hidden');
    } else if (window.currentSimStep === 2 || window.currentSimStep === 3)
    {
        if (btnBack) btnBack.classList.remove('hidden');
        if (btnCancel) btnCancel.classList.add('hidden');
        if (btnNext) btnNext.classList.remove('hidden');
        if (btnRun) btnRun.classList.add('hidden');
    } else
    {
        if (btnBack) btnBack.classList.remove('hidden');
        if (btnCancel) btnCancel.classList.add('hidden');
        if (btnNext) btnNext.classList.add('hidden');
        if (btnRun) btnRun.classList.remove('hidden');
    }

    // Progress Bar (Segmented)
    const colors = {
        1: { bar: 'bg-emerald-500', shadow: 'shadow-[0_0_10px_rgba(16,185,129,0.5)]', text: 'text-emerald-400' },
        2: { bar: 'bg-purple-500', shadow: 'shadow-[0_0_10px_rgba(168,85,247,0.5)]', text: 'text-purple-400' },
        3: { bar: 'bg-red-500', shadow: 'shadow-[0_0_10px_rgba(239,68,68,0.5)]', text: 'text-red-400' },
        4: { bar: 'bg-red-500', shadow: 'shadow-[0_0_10px_rgba(239,68,68,0.5)]', text: 'text-red-400' }
    };

    for (let i = 1; i <= 4; i++)
    {
        const bar = document.getElementById(`sim-bar-${i}`);
        const label = document.getElementById(`sim-label-${i}`);
        if (!bar || !label) continue;

        const config = colors[i];

        // Reset classes
        bar.classList.remove('bg-slate-700', 'bg-blue-500', 'bg-purple-500', 'bg-orange-500', 'bg-red-500',
            'shadow-[0_0_10px_rgba(59,130,246,0.5)]',
            'shadow-[0_0_10px_rgba(168,85,247,0.5)]',
            'shadow-[0_0_10px_rgba(249,115,22,0.5)]',
            'shadow-[0_0_10px_rgba(239,68,68,0.5)]');
        label.classList.remove('text-slate-600', 'text-blue-400', 'text-purple-400', 'text-red-400');

        if (i <= window.currentSimStep)
        {
            bar.classList.add(config.bar, config.shadow);
            label.classList.add(config.text);
        } else
        {
            bar.classList.add('bg-slate-700');
            label.classList.add('text-slate-600');
        }
    }
};

window.updateCustomCostDisplay = function ()
{
    const dirInput = document.querySelector('input[name="sim-cost-dir"]:checked');
    const pctInput = document.getElementById('sim-custom-pct');
    if (!dirInput || !pctInput) return;

    const dir = parseInt(dirInput.value);
    const pct = parseFloat(pctInput.value) || 0;
    const mult = 1 + (dir * (pct / 100));
    const resEl = document.getElementById('sim-custom-result');
    if (resEl) resEl.textContent = mult.toFixed(2) + 'x';
};

window.runSimulation = function ()
{
    // 1. Get Values
    let agrInputEl = document.querySelector('input[name="sim-agr"]:checked');
    let agrVal = agrInputEl ? agrInputEl.value : '112';
    if (agrVal === 'custom')
    {
        agrVal = document.getElementById('sim-custom-agr').value || 112;
    }

    let allocInputEl = document.querySelector('input[name="sim-alloc"]:checked');
    let allocVal = allocInputEl ? allocInputEl.value : '40';
    if (allocVal === 'custom')
    {
        allocVal = document.getElementById('sim-custom-alloc').value || 40;
    }

    let costInputEl = document.querySelector('input[name="sim-cost"]:checked');
    let costMult = costInputEl ? costInputEl.value : '1.0';
    if (costMult === 'custom')
    {
        const dirInput = document.querySelector('input[name="sim-cost-dir"]:checked');
        const dir = dirInput ? parseInt(dirInput.value) : 1;
        const pct = parseFloat(document.getElementById('sim-custom-pct').value) || 0;
        costMult = 1 + (dir * (pct / 100));
    } else
    {
        costMult = parseFloat(costMult);
    }

    // 2. Update Main Inputs
    const mainAgrInput = document.getElementById('input-agr');
    const mainAllocInput = document.getElementById('input-allocation');

    if (mainAgrInput)
    {
        window.applyAgrPreset(agrVal, 'Simulator Scenario');
    }

    if (mainAllocInput)
    {
        mainAllocInput.value = allocVal;
        mainAllocInput.dispatchEvent(new Event('input'));
    }

    // 3. Social Cost Multipliers
    const costInputs = [
        'input-cost-crime', 'input-cost-business', 'input-cost-bankruptcy',
        'input-cost-illness', 'input-cost-services', 'input-cost-abused'
    ];

    costInputs.forEach(id =>
    {
        const input = document.getElementById(id);
        if (input)
        {
            const base = parseFloat(input.dataset.default) || 0;
            const newVal = Math.round(base * costMult);
            input.value = newVal;
            input.dispatchEvent(new Event('input'));
        }
    });

    // Close Modal
    window.closeSimulatorModal();

    // Scroll to results or calculator
    const calculator = document.getElementById('calculator-controls');
    if (calculator)
    {
        calculator.scrollIntoView({ behavior: 'smooth' });
    }
};
