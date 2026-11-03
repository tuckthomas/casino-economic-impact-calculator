/* Public evidence and editable recruitment assumptions; independent of social-cost accounting. */
window.LocalEconomy = (() => {
    let countyFips = '', countyName = '', profile = null, pending = Promise.resolve(), controller = null;
    let status = 'Select a county to load its public industry and workforce evidence.';
    const escape = value => String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[character]));
    const number = value => typeof value === 'number' && Number.isFinite(value);
    const count = value => number(value) ? Math.round(value).toLocaleString('en-US') : 'Not available';
    const money = value => number(value) ? value.toLocaleString('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }) : 'Not available';
    const percent = value => number(value) ? value.toFixed(1) + '%' : 'Not available';
    const change = value => number(value) ? (value > 0 ? '+' : '') + value.toFixed(1) + '%' : 'Not available';
    const ratio = (value, base) => number(value) && number(base) && base > 0 ? value / base * 100 : null;
    const survey = estimate => !number(estimate?.value) ? 'Not available' : count(estimate.value) + (number(estimate.marginOfError) ? ' ± ' + count(estimate.marginOfError) : ' (margin unavailable)');
    const source = (url, label) => `<a href="${escape(url)}" target="_blank" rel="noopener noreferrer">${escape(label)}</a>`;
    const defaults = { positions: 1000, ftePerPosition: 1, previouslyUnemployedPercent: 25, existingEmployersPercent: 25, inCommutersPercent: 25, newResidentsPercent: 25, supportingInformation: '' };

    function hiringModel(settings, evidence) {
        const keys = ['previouslyUnemployedPercent', 'existingEmployersPercent', 'inCommutersPercent', 'newResidentsPercent'];
        const total = keys.reduce((sum, key) => sum + settings[key], 0);
        const valid = number(settings.positions) && settings.positions >= 0 && settings.positions <= 100000 && Number.isInteger(settings.positions)
            && number(settings.ftePerPosition) && settings.ftePerPosition > 0 && settings.ftePerPosition <= 1
            && keys.every(key => number(settings[key]) && settings[key] >= 0 && settings[key] <= 100)
            && Math.abs(total - 100) < 0.001;
        if (!valid) return { valid: false, error: 'Enter a whole-number position count (0–100,000), an FTE factor above 0 and at most 1, and recruitment shares totaling 100%.' };
        const unemployedHires = settings.positions * settings.previouslyUnemployedPercent / 100;
        const transfers = settings.positions * settings.existingEmployersPercent / 100;
        const commuters = settings.positions * settings.inCommutersPercent / 100;
        const newResidents = settings.positions * settings.newResidentsPercent / 100;
        const hospitality = evidence?.industries?.find(row => row.code === '72')?.jobs;
        return { valid, fte: settings.positions * settings.ftePerPosition, unemployedHires, transfers, commuters, newResidents,
            newlyEmployedResidents: unemployedHires + newResidents,
            shareOfLaborForce: ratio(settings.positions, evidence?.currentLaborMarket?.laborForce ?? evidence?.workforce?.civilianLaborForce?.value),
            shareOfUnemployed: ratio(unemployedHires, evidence?.currentLaborMarket?.unemployed ?? evidence?.workforce?.unemployed?.value),
            baselineLabel: evidence?.currentLaborMarket ? 'BLS LAUS ' + evidence.currentLaborMarket.period : 'ACS 2020–2024',
            shareOfHospitality: ratio(settings.positions, hospitality) };
    }

    function readSettings() {
        const result = { ...defaults };
        for (const key of Object.keys(defaults)) {
            const element = document.getElementById('hiring-' + key);
            if (element) result[key] = key === 'supportingInformation' ? element.value.trim().slice(0, 1000) : element.value.trim() === '' ? NaN : Number(element.value);
        }
        return result;
    }

    function evidenceNarrative(data, name, settings) {
        if (!data) return { industry: [`<strong>Public evidence status:</strong> ${escape(status)} No missing statistics are treated as zero.`], workforce: [] };
        const industries = data.industries || [];
        const sectors = industries.filter(row => row.code !== '10' && row.code.length <= 5 && !['721', '722'].includes(row.code) && number(row.jobs));
        const largest = [...sectors].sort((a, b) => b.jobs - a.jobs).slice(0, 3);
        const total = industries.find(row => row.code === '10');
        const hospitality = industries.find(row => row.code === '72');
        const manufacturing = industries.find(row => row.code === '31-33');
        const w = data.workforce;
        const model = hiringModel(settings, data);
        const qcewUrl = `https://data.bls.gov/cew/data/api/${data.industryYear}/a/area/${data.countyFips}.csv`;
        const industry = [
            `<strong>Existing employment base:</strong> ${escape(name)} has ${count(total?.jobs)} private UI-covered workplace jobs and ${count(total?.establishments)} private establishments in ${data.industryYear}. Average annual pay is ${money(total?.annualPay)} per average job. These are jobs located in the county, not a count of employed residents. Source: ${source(qcewUrl, 'BLS QCEW annual averages')}.`,
            `<strong>Largest disclosed sectors:</strong> ${largest.length ? largest.map(row => `${escape(row.name)} (${count(row.jobs)} jobs; ${percent(row.share)} of private employment)`).join('; ') : 'No sector employment estimates are available'}. Suppressed sectors cannot be ranked, so this is not necessarily a complete top-three ranking.`,
            `<strong>Employment trends:</strong> All-private employment changed ${change(total?.changeFiveYear)} from 2020 to 2025 and ${change(total?.changeTenYear)} from 2015 to 2025. Accommodation and food services changed ${change(hospitality?.changeFiveYear)} and ${change(hospitality?.changeTenYear)}, respectively. The 2020 baseline was affected by the pandemic; neither trend establishes an effect of a proposed casino.`,
            `<strong>Industry pay and concentration:</strong> Manufacturing average annual pay is ${money(manufacturing?.annualPay)}, versus ${money(hospitality?.annualPay)} in accommodation and food services. Pay is nominal annual payroll divided by average jobs, not an offered full-time salary or total compensation. Location quotients compare each sector's share of private county jobs with its share of private U.S. jobs; values above 1 indicate greater specialization.`,
            `<strong>Coverage:</strong> County industry totals are not allocated into drive-time polygons. Small-industry employment and wages may be suppressed; accommodation and food-services subindustries are already included in sector 72. NAICS revisions can affect historical comparability.`
        ];
        const workforce = [
            `<strong>Resident workforce:</strong> Census ACS 2020–2024 estimates ${survey(w?.civilianLaborForce)} civilian labor-force participants, ${survey(w?.employed)} employed residents and ${survey(w?.unemployed)} unemployed residents. Another ${survey(w?.notInLaborForce)} residents age 16+ are outside the labor force; this group is not a ready-to-hire labor pool. Counts show 90% sampling margins where available. Source: ${source('https://data.census.gov/table/ACSDT5Y2024.B23025?g=050XX00US' + data.countyFips, 'ACS B23025')}.`,
            `<strong>Existing commuting:</strong> Of ${survey(w?.residentWorkers)} resident workers age 16+, ${survey(w?.workInCounty)} work in their home county, ${survey(w?.workElsewhereInState)} work elsewhere in their state, and ${survey(w?.workOutsideState)} work outside their state. These residence-based data describe outbound work patterns; they do not measure incoming commuters, available recruits, casino patron origins or a casino's future workforce. Source: ${source('https://data.census.gov/table/ACSDT5Y2024.B08007?g=050XX00US' + data.countyFips, 'ACS B08007')}.`,
            `<strong>Wage benchmarks:</strong> ${data.occupations?.length ? `May 2025 OEWS occupational pay covers ${escape(data.wageAreaName)}, a regional geography rather than this county alone. Hourly medians and annual means are different measures. OEWS annual wages generally annualize hourly rates at 2,080 hours; they are not actual yearly earnings for part-time or seasonal workers, a casino wage commitment, or a benefits-inclusive package. Source: ${source(data.wageSourceUrl, 'BLS OEWS')}.` : 'A verified regional occupational-wage snapshot is unavailable. Industry-average pay remains a different measure and is not substituted as occupational pay.'}`
        ];
        if (data.currentLaborMarket) {
            const m = data.currentLaborMarket;
            workforce.unshift(`<strong>Latest packaged labor-market observation:</strong> BLS LAUS ${escape(m.period)} reports a resident civilian labor force of ${count(m.laborForce)}, ${count(m.employed)} employed residents, ${count(m.unemployed)} unemployed residents and ${percent(m.unemploymentRate)} unemployment, not seasonally adjusted${m.preliminary ? '; preliminary and subject to revision' : ''}. This is a monthly estimate, separate from the five-year ACS figures. Source: ${source('https://data.bls.gov/timeseries/LAUCN' + data.countyFips + '0000000003', 'BLS LAUS')}.`);
        }
        if (model.valid) {
            workforce.push(`<strong>Editable recruitment scenario:</strong> ${count(settings.positions)} positions at ${settings.ftePerPosition.toFixed(2)} FTE per position imply ${count(model.fte)} FTE. The assumed mix assigns ${count(model.unemployedHires)} hires (${percent(settings.previouslyUnemployedPercent)}) to previously unemployed county residents, ${count(model.transfers)} (${percent(settings.existingEmployersPercent)}) to existing county employers, ${count(model.commuters)} (${percent(settings.inCommutersPercent)}) to incoming commuters, and ${count(model.newResidents)} (${percent(settings.newResidentsPercent)}) to new residents. The initial 25% shares are illustrative editing defaults, not research findings or an employer staffing plan.`);
            workforce.push(`<strong>Scale and recruitment pressure:</strong> Proposed positions equal ${percent(model.shareOfLaborForce)} of the estimated county civilian labor force and ${percent(model.shareOfHospitality)} of existing private accommodation/food-service jobs. Hires assigned to unemployed residents equal ${percent(model.shareOfUnemployed)} of the unemployed estimate. Labor-force and unemployment denominators use ${escape(model.baselineLabel)}; industry jobs use 2025 QCEW. These compare different periods and populations for scale; they are not recruitment probabilities. ${model.shareOfUnemployed > 100 ? 'The assumed unemployed-resident hires exceed the baseline unemployment estimate, so that staffing allocation needs particular support. ' : ''}Transfers from existing employers may create replacement demand; the scenario does not quantify vacancies or net economy-wide job creation.`);
            workforce.push(`<strong>Employment interpretation:</strong> Under this mix, ${count(model.newlyEmployedResidents)} hires would be newly employed county residents after relocation (${count(model.unemployedHires)} previously unemployed residents plus ${count(model.newResidents)} new residents). Transfers do not by themselves add employed residents; incoming commuters reside elsewhere. These are conditional gross hiring assignments, not a measured net jobs benefit, migration forecast or dollar benefit added to the financial statements.`);
            workforce.push(`<strong>Support for hiring assumptions:</strong> ${settings.supportingInformation ? escape(settings.supportingInformation) : 'No supporting information has been supplied. Users should provide a staffing plan, recruitment evidence or citations supporting their selected position count, hours and recruitment shares.'}`);
        } else workforce.push(`<strong>Recruitment scenario incomplete:</strong> ${escape(model.error)} No hiring result is reported until the inputs are valid.`);
        workforce.push(`<strong>Evidence limitations:</strong> ACS estimates are pooled over 2020–2024 and do not represent current monthly unemployment. Sampling margins omit nonsampling error. Counts and ratios are not fitted confidence intervals for future hiring. ${data.notices.filter(note => !note.startsWith('QCEW') && !note.startsWith('ACS') && !note.startsWith('Five-year')).map(escape).join(' ')}`);
        return { industry, workforce };
    }

    function analysisHtml() {
        const sections = evidenceNarrative(profile, countyName, readSettings());
        return [['Industry Composition and Trends', sections.industry], ['Workforce and Recruitment Analysis', sections.workforce]]
            .filter(([, lines]) => lines.length).map(([title, lines]) => `<div class="font-bold text-white mb-2 uppercase tracking-wide text-sm underline">${title}</div><ul class="list-disc pl-8 space-y-3 mb-4 text-slate-300">${lines.map(line => `<li>${line}</li>`).join('')}</ul>`).join('');
    }

    const table = (caption, headers, rows) => `<div class="local-economy-table-scroll" tabindex="0" role="region" aria-label="${escape(caption)}"><table><caption>${escape(caption)}</caption><thead><tr>${headers.map(label => `<th scope="col">${escape(label)}</th>`).join('')}</tr></thead><tbody>${rows.map(row => `<tr>${row.map((cell, index) => index === 0 ? `<th scope="row">${cell}</th>` : `<td>${cell}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;

    function render() {
        const evidence = document.getElementById('local-economy-evidence');
        if (!evidence) return;
        const title = document.getElementById('local-economy-county');
        if (title) title.textContent = countyName || 'County evidence';
        const hiring = document.getElementById('local-hiring-controls');
        if (hiring) hiring.hidden = !profile;
        if (!profile) { evidence.innerHTML = `<p role="status">${escape(status)}</p>`; return; }
        const total = profile.industries.find(row => row.code === '10');
        const w = profile.workforce;
        let html = `<div class="local-economy-metrics">${[
            ['Private workplace jobs · 2025', count(total?.jobs)], ['Private annual pay · 2025', money(total?.annualPay)],
            ['Civilian labor force · 2020–2024', survey(w?.civilianLaborForce)], ['Unemployed residents · 2020–2024', survey(w?.unemployed)]
        ].map(([label, value]) => `<div><span>${label}</span><strong>${value}</strong></div>`).join('')}</div>`;
        html += '<h4>Industry composition and trends</h4><p>Private UI-covered jobs located in the selected county. Growth compares annual average employment; pay is nominal annual payroll per average job.</p>';
        const rows = profile.industries.filter(row => row.code !== '10').sort((a, b) => (b.jobs ?? -1) - (a.jobs ?? -1));
        html += table('Private industries · 2025 annual averages', ['Industry / NAICS', 'Jobs', 'Establishments', 'Share', 'Annual pay', '2020–25 jobs', '2015–25 jobs', 'U.S. LQ'], rows.map(row => [
            `${escape(row.name)} <span class="local-economy-code">${escape(row.code)}${row.suppressed ? ' · suppressed' : ''}</span>`, count(row.jobs), count(row.establishments), percent(row.share), money(row.annualPay), change(row.changeFiveYear), change(row.changeTenYear), number(row.locationQuotient) ? row.locationQuotient.toFixed(2) : 'Not available'
        ]));
        html += '<p>Location quotient (LQ): sector share of private county jobs divided by its share of private U.S. jobs. Above 1 means greater specialization. Subindustries 721 and 722 are included in 72. Suppressed or unavailable values are not zero. The 2020 comparison starts in a pandemic year.</p>';
        html += '<h4>Resident workforce and commuting</h4>';
        if (profile.currentLaborMarket) {
            const monthly = profile.currentLaborMarket;
            html += `<p><strong>BLS LAUS ${escape(monthly.period)}:</strong> ${count(monthly.laborForce)} in the civilian labor force; ${count(monthly.employed)} employed; ${count(monthly.unemployed)} unemployed; ${percent(monthly.unemploymentRate)} unemployment. Not seasonally adjusted${monthly.preliminary ? '; preliminary' : ''}. Latest monthly observation included in this release; separate from ACS below.</p>`;
        }
        html += table('Census ACS 2020–2024 · age 16+ · estimate ± 90% margin', ['Resident measure', 'People'], [
            ['Population age 16+', survey(w?.population16Plus)], ['Civilian labor force', survey(w?.civilianLaborForce)], ['Employed residents', survey(w?.employed)], ['Unemployed residents', survey(w?.unemployed)], ['Not in the labor force', survey(w?.notInLaborForce)], ['Resident workers', survey(w?.residentWorkers)], ['Work in home county', survey(w?.workInCounty)], ['Work elsewhere in home state', survey(w?.workElsewhereInState)], ['Work outside home state', survey(w?.workOutsideState)]
        ]);
        html += '<p>Commuting figures describe where county residents work, not incoming workers or available recruits. People outside the labor force are not automatically available for hire. ACS periods, QCEW workplaces and the map’s exposed adult population have different meanings.</p>';
        if (profile.occupations.length) {
            html += `<h4>Occupational wage benchmarks</h4><p>May 2025 · ${escape(profile.wageAreaName)}. Regional estimates, not county-only wages or casino pay offers. Hourly medians and annual means are different statistics. Annual wages generally assume 2,080 hours; they are not actual yearly earnings for part-time or seasonal workers and exclude benefits.</p>`;
            html += table('Regional occupational wages · BLS OEWS', ['Occupation', 'Median hourly', 'Mean annual'], profile.occupations.map(row => [escape(row.name), number(row.medianHourly) ? '$' + row.medianHourly.toFixed(2) : 'Not available', money(row.meanAnnual)]));
        }
        html += `<details class="local-economy-sources"><summary>Sources, data periods and coverage</summary><p>Public data retrieved ${escape(profile.retrievedAtUtc.slice(0, 10))}. Industry vintage: ${profile.industryYear}; workforce period: 2020–2024; wage benchmarks: May 2025. ${profile.packagedIndustrySnapshot ? 'Versioned source snapshots are included with this release for reproducibility.' : 'County industry data were retrieved from BLS for this request.'}</p><ul>${profile.sourceUrls.map(url => `<li>${source(url, url.includes('b23025') ? 'Census B23025 · labor force' : url.includes('b08007') ? 'Census B08007 · place of work' : url.includes('/oes/') ? 'BLS regional occupational wages' : 'BLS QCEW · ' + url.split('/api/')[1])}</li>`).join('')}</ul>${profile.notices.map(note => `<p>${escape(note)}</p>`).join('')}</details>`;
        evidence.innerHTML = html;
        renderHiring();
    }

    function renderHiring() {
        const output = document.getElementById('local-hiring-output');
        if (!output) return;
        const settings = readSettings(), model = hiringModel(settings, profile);
        if (!model.valid) { output.innerHTML = `<p role="alert" class="local-economy-error">${escape(model.error)}</p>`; return; }
        output.innerHTML = table('Illustrative hiring assignments · selected assumptions', ['Recruitment source', 'Share', 'Hires'], [
            ['Previously unemployed county residents', percent(settings.previouslyUnemployedPercent), count(model.unemployedHires)],
            ['Transfers from existing county employers', percent(settings.existingEmployersPercent), count(model.transfers)],
            ['Incoming commuters residing elsewhere', percent(settings.inCommutersPercent), count(model.commuters)],
            ['New residents relocating to the county', percent(settings.newResidentsPercent), count(model.newResidents)]
        ]) + `<p><strong>${count(model.fte)} FTE</strong> from ${count(settings.positions)} positions. Positions equal ${percent(model.shareOfLaborForce)} of the civilian labor force and ${percent(model.shareOfHospitality)} of private accommodation/food-service jobs. Assigned unemployed-resident hires equal ${percent(model.shareOfUnemployed)} of the unemployed estimate. Labor-force baseline: ${escape(model.baselineLabel)}; industry baseline: 2025 QCEW.</p>${model.shareOfUnemployed > 100 ? '<p class="local-economy-error">Assigned unemployed-resident hires exceed the baseline unemployment estimate. Supply evidence for this allocation or revise the recruitment mix.</p>' : ''}<p>Hires may fill part-time or full-time positions. Each position is assumed to be filled by a different person in this illustration. Counts are rounded independently and may not sum exactly. Recruitment assignments do not establish net jobs, vacancies, migration or economic benefits and are not added to the financial balance.</p>`;
    }

    function bind() {
        const panel = document.getElementById('local-economy-panel');
        if (!panel || panel.dataset.bound) return;
        panel.dataset.bound = 'true';
        panel.addEventListener('input', event => {
            if (!event.target.id?.startsWith('hiring-')) return;
            renderHiring();
            window.EconomicCalculator?.calculate();
        });
    }

    function update(fips, name) {
        bind();
        if (countyFips === fips) return;
        countyFips = fips || ''; countyName = name || ''; profile = null;
        controller?.abort(); controller = new AbortController();
        status = fips ? 'Loading public industry and workforce evidence…' : 'Select a county to load its public industry and workforce evidence.';
        render();
        if (!fips) { pending = Promise.resolve(); return; }
        const selected = fips;
        pending = fetch('/api/local-economy/' + encodeURIComponent(fips), { signal: controller.signal })
            .then(response => { if (!response.ok) throw new Error('Public evidence is unavailable'); return response.json(); })
            .then(data => { if (countyFips === selected && data.countyFips === selected) { profile = data; status = ''; render(); window.EconomicCalculator?.calculate(); } })
            .catch(error => { if (error.name !== 'AbortError' && countyFips === selected) { status = 'Public economic evidence could not be loaded. Reselect the county to retry. No missing data have been substituted.'; render(); window.EconomicCalculator?.calculate(); } });
    }

    async function ensureReady() {
        // Follow the current county if selection changes while an older request is pending.
        let request;
        do { request = pending; await request; } while (request !== pending);
        if (profile && !hiringModel(readSettings(), profile).valid) throw new Error('Correct the recruitment inputs before exporting.');
    }

    function getReport() { return { profile, hiring: readSettings(), statusNote: status }; }
    return { update, analysisHtml, ensureReady, getReport, hiringModel, evidenceNarrative };
})();
