window.SlotMachine = (function ()
{
    const BAIT_WORDS = [
        'WORLD-CLASS DESTINATION', 'INCREDIBLE OPPORTUNITY', 'ECONOMIC ENGINE',
        'REVITALIZED INFRASTRUCTURE', 'NEIGHBORHOOD IMPROVEMENTS', 'HUMANITARIAN FUND',
        'RESPONSIBLE GAMING', 'RETAIN TALENT', 'COMMUNITY WELL-BEING', 'PREMIER DESTINATION', 'GOOD PAYING JOBS'
    ];

    const TARGET_WORDS = [
        'ADDICTION', 'HUMAN TRAFFICKING', 'EMBEZZLEMENT', 'POVERTY', 'CHILD NEGLECT', 'DOMESTIC VIOLENCE',
        'BANKRUPTCIES', 'DIVORCES', 'SUBSTANCE ABUSE', 'HOMELESSNESS', 'FORECLOSURE', 'CORRUPTION', 'BAD DEBT',
        'FATAL ACCIDENTS', 'LOCAL BUSINESSES LOST', 'MENTAL HEALTH ISSUES', 'LOW WAGE JOBS'
    ];

    const ICONS = {
        'WORLD-CLASS DESTINATION': 'public',
        'INCREDIBLE OPPORTUNITY': 'auto_awesome',
        'ECONOMIC ENGINE': 'trending_up',
        'REVITALIZED INFRASTRUCTURE': 'construction',
        'NEIGHBORHOOD IMPROVEMENTS': 'home_work',
        'HUMANITARIAN FUND': 'volunteer_activism',
        'RESPONSIBLE GAMING': 'verified_user',
        'RETAIN TALENT': 'groups',
        'COMMUNITY WELL-BEING': 'favorite',
        'PREMIER DESTINATION': 'diamond',
        'GOOD PAYING JOBS': 'work',
        'ADDICTION': 'warning',
        'HUMAN TRAFFICKING': 'person_alert',
        'EMBEZZLEMENT': 'money_off',
        'POVERTY': 'trending_down',
        'CHILD NEGLECT': 'family_restroom',
        'DOMESTIC VIOLENCE': 'personal_injury',
        'BANKRUPTCIES': 'account_balance',
        'DIVORCES': 'heart_broken',
        'SUBSTANCE ABUSE': 'medication',
        'HOMELESSNESS': 'house',
        'FORECLOSURE': 'key_off',
        'CORRUPTION': 'gavel',
        'BAD DEBT': 'credit_card_off',
        'FATAL ACCIDENTS': 'car_crash',
        'LOCAL BUSINESSES LOST': 'storefront',
        'MENTAL HEALTH ISSUES': 'psychology',
        'LOW WAGE JOBS': 'payments'
    };

    const INITIAL_COLUMNS = [
        ['PREMIER DESTINATION', 'WORLD-CLASS DESTINATION', 'RETAIN TALENT'],
        ['COMMUNITY WELL-BEING', 'ECONOMIC ENGINE', 'GOOD PAYING JOBS'],
        ['NEIGHBORHOOD IMPROVEMENTS', 'HUMANITARIAN FUND', 'RESPONSIBLE GAMING']
    ];

    const state = {
        credits: 2,
        phase: 'idle',
        columns: []
    };

    let isInitialized = false;
    let deferredInitTimer = null;
    let sequenceMobileMaxWidth = 1023;
    let earlyGateObserver = null;
    let mobileSequenceReady = false;
    let slotIsVisible = true;
    let slotVisibilityObserver = null;
    let heroSwipeLayout = null;
    let heroSwipeBound = false;
    let heroTouchStartX = 0;
    let heroTouchStartY = 0;
    let heroTouchTracking = false;
    let heroTouchHandled = false;
    let sirenTimeout = null;
    let resultTimeout = null;
    let alarmTimeout = null;

    function isMobileViewport(maxWidth = sequenceMobileMaxWidth)
    {
        return window.matchMedia(`(max-width: ${maxWidth}px)`).matches;
    }

    function prefersReducedMotion()
    {
        return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    function canAnimate()
    {
        return !document.hidden && slotIsVisible && !prefersReducedMotion();
    }

    function getCabinet()
    {
        return document.querySelector('#hero-section [data-slot-machine]');
    }

    function setStatus(message)
    {
        const status = document.getElementById('slot-status');
        if (status) status.textContent = message;
    }

    function setCabinetPhase(phase)
    {
        const cabinet = getCabinet();
        if (!cabinet) return;

        cabinet.classList.remove('is-spinning', 'is-result', 'is-credit-alarm', 'is-coin-inserting');
        if (phase === 'spinning') cabinet.classList.add('is-spinning');
        if (phase === 'result') cabinet.classList.add('is-result');
        if (phase === 'credit-alarm') cabinet.classList.add('is-credit-alarm');
        if (phase === 'coin-inserting') cabinet.classList.add('is-coin-inserting');
    }

    function clearResultState()
    {
        const cabinet = getCabinet();
        if (cabinet) cabinet.classList.remove('is-result');
        if (resultTimeout)
        {
            clearTimeout(resultTimeout);
            resultTimeout = null;
        }
    }

    function initSlotVisibilityTracking()
    {
        if (slotVisibilityObserver) return;
        const slotShell = document.querySelector('#hero-section .hero-slot-shell');
        if (!slotShell) return;

        if (typeof IntersectionObserver === 'undefined')
        {
            slotIsVisible = true;
            return;
        }

        slotVisibilityObserver = new IntersectionObserver((entries) =>
        {
            slotIsVisible = entries.some((entry) => entry.isIntersecting);
        }, { threshold: 0.01 });
        slotVisibilityObserver.observe(slotShell);
    }

    function getGateTargets()
    {
        const targets = new Set();
        ['#hero-section .mobile-sequence-gated', '#hero-section .hero-slot-shell'].forEach((selector) =>
        {
            document.querySelectorAll(selector).forEach((element) => targets.add(element));
        });
        return Array.from(targets);
    }

    function resetGatedInlineStyles()
    {
        const heroSection = document.getElementById('hero-section');
        if (!heroSection) return;

        const copy = heroSection.querySelector('.hero-copy');
        if (copy)
        {
            copy.style.opacity = '';
            copy.style.pointerEvents = '';
        }

        getGateTargets().forEach((element) =>
        {
            element.style.opacity = '';
            element.style.visibility = '';
            element.style.pointerEvents = '';
            element.style.display = '';
            element.style.transform = '';
            element.style.width = '';
            element.style.maxWidth = '';
            element.style.paddingTop = '';
            element.style.marginTop = '';
        });
    }

    function applyEarlyMobileGate()
    {
        if (!isMobileViewport() || mobileSequenceReady) return;
        resetGatedInlineStyles();
    }

    function initEarlyMobileGate()
    {
        if (earlyGateObserver) return;
        applyEarlyMobileGate();

        const appRoot = document.getElementById('app') || document.body;
        if (!appRoot) return;

        earlyGateObserver = new MutationObserver(() => applyEarlyMobileGate());
        earlyGateObserver.observe(appRoot, { childList: true, subtree: true });
    }

    function isInteractiveHeroTarget(target)
    {
        return Boolean(target && target.closest && target.closest('button, a, input, select, textarea, [role="button"]'));
    }

    function onHeroSwipeTouchStart(e)
    {
        if (!isMobileViewport() || !e || !e.touches || e.touches.length !== 1) return;
        if (isInteractiveHeroTarget(e.target)) return;

        const layout = document.querySelector('#hero-section .hero-layout');
        if (!layout || !layout.contains(e.target)) return;

        heroSwipeLayout = layout;
        heroTouchStartX = e.touches[0].clientX;
        heroTouchStartY = e.touches[0].clientY;
        heroTouchTracking = true;
        heroTouchHandled = false;
    }

    function navigateHeroBySwipe(layout, dx)
    {
        if (!layout) return false;

        const panelWidth = Math.max(1, layout.clientWidth);
        const panelCount = Math.max(1, layout.children.length);
        const maxLeft = Math.max(0, (panelCount - 1) * panelWidth);
        const currentLeft = Math.max(0, Math.min(layout.scrollLeft, maxLeft));
        const targetLeft = dx < 0 && currentLeft < maxLeft - 8
            ? maxLeft
            : (dx > 0 && currentLeft > 8 ? 0 : currentLeft);

        if (Math.abs(targetLeft - currentLeft) < 1) return false;
        layout.scrollTo({ left: targetLeft, behavior: 'smooth' });
        return true;
    }

    function onHeroSwipeTouchMove(e)
    {
        if (!heroTouchTracking || !isMobileViewport() || !e || !e.touches || e.touches.length === 0) return;
        if (heroTouchHandled)
        {
            if (e.cancelable) e.preventDefault();
            return;
        }

        const moveTouch = e.touches[0];
        const dx = moveTouch.clientX - heroTouchStartX;
        const dy = moveTouch.clientY - heroTouchStartY;
        if (Math.abs(dx) < 28 || Math.abs(dx) <= Math.abs(dy) * 1.05) return;

        const layout = heroSwipeLayout || document.querySelector('#hero-section .hero-layout');
        if (!layout) return;
        if (e.cancelable) e.preventDefault();
        heroTouchHandled = navigateHeroBySwipe(layout, dx);
        if (heroTouchHandled) heroSwipeLayout = layout;
    }

    function onHeroSwipeTouchEnd(e)
    {
        if (!heroTouchTracking || !isMobileViewport()) return;
        heroTouchTracking = false;

        if (heroTouchHandled)
        {
            heroTouchHandled = false;
            heroSwipeLayout = null;
            return;
        }

        if (!e || !e.changedTouches || e.changedTouches.length === 0) return;
        const endTouch = e.changedTouches[0];
        const dx = endTouch.clientX - heroTouchStartX;
        const dy = endTouch.clientY - heroTouchStartY;
        if (Math.abs(dx) < 42 || Math.abs(dx) < Math.abs(dy) * 1.1) return;

        const layout = heroSwipeLayout || document.querySelector('#hero-section .hero-layout');
        heroSwipeLayout = null;
        if (layout) navigateHeroBySwipe(layout, dx);
    }

    function setupHeroSwipeNavigation()
    {
        if (heroSwipeBound) return;
        document.addEventListener('touchstart', onHeroSwipeTouchStart, { passive: true, capture: true });
        document.addEventListener('touchmove', onHeroSwipeTouchMove, { passive: false, capture: true });
        document.addEventListener('touchend', onHeroSwipeTouchEnd, { passive: true, capture: true });
        document.addEventListener('touchcancel', () =>
        {
            heroTouchTracking = false;
            heroTouchHandled = false;
            heroSwipeLayout = null;
        }, { passive: true, capture: true });
        heroSwipeBound = true;
    }

    function outcome(label, type)
    {
        return { label: label, type: type, icon: ICONS[label] || (type === 'truth' ? 'warning' : 'star') };
    }

    function randomFrom(items)
    {
        return items[Math.floor(Math.random() * items.length)];
    }

    function randomBait(exclude = [])
    {
        const choices = BAIT_WORDS.filter((label) => !exclude.includes(label));
        return randomFrom(choices.length ? choices : BAIT_WORDS);
    }

    function randomTruth(exclude = [])
    {
        const choices = TARGET_WORDS.filter((label) => !exclude.includes(label));
        return randomFrom(choices.length ? choices : TARGET_WORDS);
    }

    function createInitialColumns()
    {
        return INITIAL_COLUMNS.map((column) => column.map((label, row) => outcome(label, row === 1 ? 'bait' : 'near-miss')));
    }

    function createFinalColumns()
    {
        const usedTruth = [];
        return [0, 1, 2].map(() =>
        {
            const center = randomTruth(usedTruth);
            usedTruth.push(center);
            const top = randomBait();
            const bottom = randomBait([top]);
            return [outcome(top, 'near-miss'), outcome(center, 'truth'), outcome(bottom, 'near-miss')];
        });
    }

    function createCellElement(tile, row)
    {
        const cell = document.createElement('div');
        cell.className = 'modern-slot-cell' + (tile.type === 'truth' ? ' is-truth' : (tile.type === 'near-miss' ? ' is-near-miss' : ''));
        if (typeof row === 'number') cell.dataset.slotRow = String(row);
        cell.dataset.slotType = tile.type;

        const icon = document.createElement('span');
        icon.className = 'material-symbols-outlined modern-slot-cell-icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = tile.icon || (tile.type === 'truth' ? 'warning' : 'star');

        const label = document.createElement('span');
        label.className = 'modern-slot-cell-label';
        label.textContent = tile.label;

        cell.appendChild(icon);
        cell.appendChild(label);
        return cell;
    }

    function getStripElement(index)
    {
        return document.querySelector(`[data-slot-strip="${index}"]`) ||
               document.querySelector(`[data-slot-column="${index}"] .modern-slot-reel-strip`) ||
               document.querySelector(`[data-slot-column="${index}"]`);
    }

    function renderColumnStrip(index, columnTiles)
    {
        const strip = getStripElement(index);
        if (!strip) return;

        strip.innerHTML = '';
        strip.style.transition = 'none';
        strip.style.transform = 'translate3d(0, 0, 0)';
        columnTiles.forEach((tile, row) =>
        {
            strip.appendChild(createCellElement(tile, row));
        });
    }

    function renderAllColumns(columns)
    {
        columns.forEach((column, index) => renderColumnStrip(index, column));
    }

    function updateCreditDisplay()
    {
        const display = document.getElementById('credit-display');
        if (display) display.textContent = String(state.credits).padStart(2, '0');
    }

    function updateControls()
    {
        const spinButton = document.getElementById('slot-spin-button');
        const coinButton = document.getElementById('slot-coin-insert');
        const busy = state.phase === 'spinning';
        const inserting = state.phase === 'coin-inserting';
        if (spinButton) spinButton.disabled = busy || inserting;
        if (coinButton) coinButton.disabled = busy || inserting;
    }

    function resetSiren()
    {
        const siren = document.getElementById('siren');
        if (siren) siren.classList.remove('is-active', 'is-alarm-active');
        if (sirenTimeout)
        {
            clearTimeout(sirenTimeout);
            sirenTimeout = null;
        }
    }

    function triggerSirenSequence()
    {
        const siren = document.getElementById('siren');
        if (!siren) return;
        siren.classList.add('is-active');
        if (sirenTimeout) clearTimeout(sirenTimeout);
        sirenTimeout = setTimeout(() =>
        {
            siren.classList.remove('is-active');
            sirenTimeout = null;
        }, prefersReducedMotion() ? 900 : 3000);
    }

    function clearCreditAlarm()
    {
        const cabinet = getCabinet();
        const coinButton = document.getElementById('slot-coin-insert');
        const siren = document.getElementById('siren');
        if (cabinet) cabinet.classList.remove('is-credit-alarm');
        if (coinButton) coinButton.classList.remove('is-attention');
        if (siren) siren.classList.remove('is-alarm-active');
        if (alarmTimeout)
        {
            clearTimeout(alarmTimeout);
            alarmTimeout = null;
        }
    }

    function triggerCreditAlarm()
    {
        if (state.phase === 'spinning' || state.phase === 'coin-inserting') return;
        clearResultState();
        clearCreditAlarm();
        state.phase = 'credit-alarm';
        setCabinetPhase('credit-alarm');
        setStatus('NO CREDITS — INSERT COIN');

        const coinButton = document.getElementById('slot-coin-insert');
        const siren = document.getElementById('siren');
        if (coinButton) coinButton.classList.add('is-attention');
        if (siren) siren.classList.add('is-alarm-active');

        alarmTimeout = setTimeout(() =>
        {
            clearCreditAlarm();
            if (state.phase === 'credit-alarm')
            {
                state.phase = 'idle';
                setCabinetPhase('idle');
                setStatus('INSERT COIN TO PLAY');
                updateControls();
            }
        }, 2800);
    }

    function animateReelStrip(index, finalColumn, duration, intermediateCount)
    {
        return new Promise((resolve) =>
        {
            const strip = getStripElement(index);
            if (!strip)
            {
                resolve();
                return;
            }

            if (!canAnimate())
            {
                renderColumnStrip(index, finalColumn);
                state.columns[index] = finalColumn;
                resolve();
                return;
            }

            const currentTiles = state.columns[index] || createInitialColumns()[index];
            const intermediateTiles = [];
            for (let i = 0; i < intermediateCount; i++)
            {
                const isTruth = Math.random() > 0.55;
                intermediateTiles.push(isTruth ? outcome(randomTruth(), 'truth') : outcome(randomBait(), 'near-miss'));
            }
            const allTiles = [...finalColumn, ...intermediateTiles, ...currentTiles];

            strip.innerHTML = '';
            allTiles.forEach((tile, i) =>
            {
                const isTargetRow = i < 3 ? i : (i >= allTiles.length - 3 ? i - (allTiles.length - 3) : undefined);
                strip.appendChild(createCellElement(tile, isTargetRow));
            });

            const firstCell = strip.children[0];
            const cellRect = firstCell ? firstCell.getBoundingClientRect() : null;
            const cellHeight = (cellRect && cellRect.height > 0) ? cellRect.height : (firstCell ? firstCell.offsetHeight : 90);
            const gap = 2;
            const step = cellHeight + gap;
            const scrollDistance = (allTiles.length - 3) * step;

            strip.style.transition = 'none';
            strip.style.transform = `translate3d(0, -${scrollDistance}px, 0)`;
            strip.classList.add('is-spinning-strip');
            strip.offsetHeight;

            strip.style.transition = `transform ${duration}ms cubic-bezier(0.16, 1, 0.3, 1)`;
            strip.style.transform = 'translate3d(0, 0, 0)';

            setTimeout(() =>
            {
                strip.classList.remove('is-spinning-strip');
                renderColumnStrip(index, finalColumn);
                state.columns[index] = finalColumn;
                resolve();
            }, duration);
        });
    }

    async function requestSpin()
    {
        if (state.phase === 'spinning' || state.phase === 'coin-inserting') return;
        if (state.credits <= 0)
        {
            triggerCreditAlarm();
            return;
        }

        clearCreditAlarm();
        clearResultState();
        resetSiren();
        state.credits--;
        state.phase = 'spinning';
        updateCreditDisplay();
        updateControls();
        setCabinetPhase('spinning');
        setStatus('SPINNING');

        const finalColumns = createFinalColumns();
        const reelConfigs = [
            { duration: prefersReducedMotion() ? 160 : 1250, intermediate: 14 },
            { duration: prefersReducedMotion() ? 160 : 1900, intermediate: 22 },
            { duration: prefersReducedMotion() ? 160 : 2550, intermediate: 30 }
        ];

        await Promise.all(finalColumns.map((column, index) =>
            animateReelStrip(index, column, reelConfigs[index].duration, reelConfigs[index].intermediate)));

        state.columns = finalColumns;
        state.phase = 'result';
        setCabinetPhase('result');
        updateControls();
        triggerSirenSequence();

        const result = finalColumns.map((column) => column[1].label).join(' • ');
        setStatus(`COMMUNITY OUTCOME: ${result}`);
        resultTimeout = setTimeout(() =>
        {
            const cabinet = getCabinet();
            if (cabinet) cabinet.classList.remove('is-result');
            resultTimeout = null;
        }, 3600);
    }

    function insertCoin()
    {
        if (state.phase === 'spinning' || state.phase === 'coin-inserting') return;
        clearCreditAlarm();
        clearResultState();
        state.phase = 'coin-inserting';
        setCabinetPhase('coin-inserting');
        setStatus('COIN ACCEPTED');
        updateControls();

        const slotEl = document.querySelector('.modern-slot-coin-slot') || document.getElementById('slot-coin-insert');
        const button = document.getElementById('slot-coin-insert');
        if (button) button.classList.add('is-inserting');

        if (!slotEl || prefersReducedMotion())
        {
            setTimeout(() =>
            {
                state.credits++;
                updateCreditDisplay();
                if (button) button.classList.remove('is-inserting', 'is-attention');
                state.phase = 'idle';
                setCabinetPhase('idle');
                setStatus('CREDIT ADDED — READY');
                updateControls();
            }, prefersReducedMotion() ? 160 : 600);
            return;
        }

        const rect = slotEl.getBoundingClientRect();
        const shell = document.querySelector('.hero-slot-shell') || document.querySelector('.modern-slot-machine') || document.body;
        const shellRect = shell.getBoundingClientRect();

        const coinW = 24;
        const left = (rect.left - shellRect.left) + (rect.width / 2) - (coinW / 2);
        const top = (rect.top - shellRect.top) + (rect.height / 2) - (coinW / 2);

        const scene = document.createElement('div');
        scene.className = 'modern-coin-scene';
        scene.style.position = 'absolute';
        scene.style.left = `${left}px`;
        scene.style.top = `${top}px`;
        scene.style.width = `${coinW}px`;
        scene.style.height = `${coinW}px`;
        scene.style.zIndex = '99999';
        scene.style.pointerEvents = 'none';
        scene.style.perspective = '2000px';

        const coinWrapper = document.createElement('div');
        coinWrapper.className = 'modern-coin-wrapper anim-insert';
        coinWrapper.style.position = 'absolute';
        coinWrapper.style.top = '0';
        coinWrapper.style.left = '0';

        const isHeads = Math.random() > 0.5;

        for (let i = -2; i <= 2; i++)
        {
            const layer = document.createElement('div');
            layer.className = 'modern-coin-layer';

            let transform = `translateZ(${i * 4}px)`;
            if (i === -2) transform += ' rotateY(180deg)';
            layer.style.transform = transform;

            if (Math.abs(i) === 2)
            {
                layer.classList.add('modern-coin-face');
                if (i === 2) layer.classList.add(isHeads ? 'modern-coin-face-front' : 'modern-coin-face-back');
                if (i === -2) layer.classList.add(isHeads ? 'modern-coin-face-back' : 'modern-coin-face-front');
            }
            else
            {
                layer.classList.add('modern-coin-edge');
            }

            coinWrapper.appendChild(layer);
        }

        scene.appendChild(coinWrapper);
        shell.appendChild(scene);

        setTimeout(() =>
        {
            if (scene.parentNode) scene.parentNode.removeChild(scene);
            state.credits++;
            updateCreditDisplay();
            if (button) button.classList.remove('is-inserting', 'is-attention');
            state.phase = 'idle';
            setCabinetPhase('idle');
            setStatus('CREDIT ADDED — READY');
            updateControls();
        }, 1400);
    }

    function bindControls()
    {
        const spinButton = document.getElementById('slot-spin-button');
        const coinButton = document.getElementById('slot-coin-insert');
        if (spinButton && spinButton.dataset.slotBound !== 'true')
        {
            spinButton.addEventListener('click', requestSpin);
            spinButton.dataset.slotBound = 'true';
        }
        if (coinButton && coinButton.dataset.slotBound !== 'true')
        {
            coinButton.addEventListener('click', insertCoin);
            coinButton.dataset.slotBound = 'true';
        }
    }

    function setMobileSequenceState(stateName)
    {
        const heroSection = document.getElementById('hero-section');
        if (!heroSection) return;
        heroSection.classList.remove('mobile-sequence-pending', 'mobile-sequence-ready');

        if (stateName === 'pending')
        {
            mobileSequenceReady = false;
            heroSection.classList.add('mobile-sequence-pending');
            resetGatedInlineStyles();
            return;
        }

        mobileSequenceReady = true;
        heroSection.classList.add('mobile-sequence-ready');
        resetGatedInlineStyles();
    }

    function initializeMachine()
    {
        if (isInitialized) return;
        const cabinet = getCabinet();
        if (!cabinet) return;

        isInitialized = true;
        initSlotVisibilityTracking();
        state.columns = createInitialColumns();
        renderAllColumns(state.columns);
        updateCreditDisplay();
        bindControls();
        updateControls();
        setCabinetPhase('idle');
        setStatus('READY');
    }

    function init(optionsOrMobileMaxWidth, mobileRevealDelayMsArg)
    {
        let mobileMaxWidth = 1023;
        let mobileRevealDelayMs = 0;

        if (optionsOrMobileMaxWidth && typeof optionsOrMobileMaxWidth === 'object')
        {
            const opts = optionsOrMobileMaxWidth;
            mobileMaxWidth = Number.isFinite(Number(opts.mobileMaxWidth)) ? Number(opts.mobileMaxWidth) : mobileMaxWidth;
            mobileRevealDelayMs = Number.isFinite(Number(opts.mobileRevealDelayMs)) ? Number(opts.mobileRevealDelayMs) : mobileRevealDelayMs;
        } else
        {
            mobileMaxWidth = Number.isFinite(Number(optionsOrMobileMaxWidth)) ? Number(optionsOrMobileMaxWidth) : mobileMaxWidth;
            mobileRevealDelayMs = Number.isFinite(Number(mobileRevealDelayMsArg)) ? Number(mobileRevealDelayMsArg) : mobileRevealDelayMs;
        }

        sequenceMobileMaxWidth = mobileMaxWidth;
        mobileSequenceReady = true;
        setupHeroSwipeNavigation();

        if (deferredInitTimer)
        {
            clearTimeout(deferredInitTimer);
            deferredInitTimer = null;
        }

        setMobileSequenceState('ready');
        initializeMachine();
    }

    initEarlyMobileGate();

    return {
        init: init,
        insertCoin: insertCoin
    };
})();

if (typeof window.slotMachineSafeInit !== 'function')
{
    window.slotMachineSafeInit = function (mobileMaxWidth, mobileRevealDelayMs)
    {
        if (window.SlotMachine && typeof window.SlotMachine.init === 'function')
        {
            window.SlotMachine.init(mobileMaxWidth, mobileRevealDelayMs);
        }
    };
}