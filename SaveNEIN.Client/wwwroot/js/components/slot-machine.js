window.SlotMachine = (function ()
{
    const ITEM_HEIGHT = 80;
    const SLOT_COUNT = 15; // Total items on the ring
    const TARGET_INDEX = 8; // Where the "Truth" lands (Rigged)
    // Radius calculation for a Polygon/Cylinder
    // r = (w / 2) / tan(PI / n)
    // w = height of item here because we rotateX
    const RADIUS = Math.round((ITEM_HEIGHT / 2) / Math.tan(Math.PI / SLOT_COUNT));

    const BAIT_WORDS = [
        "WORLD-CLASS DESTINATION", "INCREDIBLE OPPORTUNITY", "ECONOMIC ENGINE",
        "REVITALIZED INFRASTRUCTURE", "NEIGHBORHOOD IMPROVEMENTS", "HUMANITARIAN FUND",
        "RESPONSIBLE GAMING", "RETAIN TALENT", "COMMUNITY WELL-BEING", "PREMIER DESTINATION", "GOOD PAYING JOBS"
    ];
    const TARGET_WORDS = [
        "ADDICTION", "HUMAN TRAFFICKING", "EMBEZZLEMENT", "POVERTY", "CHILD NEGLECT", "DOMESTIC VIOLENCE",
        "BANKRUPTCIES", "DIVORCES", "SUBSTANCE ABUSE", "HOMELESSNESS", "FORECLOSURE", "CORRUPTION", "BAD DEBT",
        "FATAL ACCIDENTS", "LOCAL BUSINESSES LOST", "MENTAL HEALTH ISSUES", "LOW WAGE JOBS"
    ]; // The forced outcomes (Negative)

    let currentRotation = [0, 0, 0]; // Track rotation per reel
    let isAtFront = true; // State to toggle between index 0 and 8
    let isSpinning = false;
    let isSirenActive = false;
    let lightAnimationId = null;
    let sirenTimeout = null;

    // Credit System
    let currentCredits = 2;
    let isAlarmActive = false;
    let isInitialized = false;
    let deferredInitTimer = null;
    let resizeObserver = null;
    let sequenceMobileMaxWidth = 1023;
    let earlyGateObserver = null;
    let mobileSequenceReady = false;
    let slotIsVisible = true;
    let slotVisibilityObserver = null;
    let lightLastFrameTime = 0;
    let heroSwipeLayout = null;
    let heroSwipeBound = false;
    let heroTouchStartX = 0;
    let heroTouchStartY = 0;
    let heroTouchTracking = false;
    let heroTouchHandled = false;

    function isMobileViewport(maxWidth = sequenceMobileMaxWidth)
    {
        return window.matchMedia(`(max-width: ${maxWidth}px)`).matches;
    }

    function getLightFrameInterval()
    {
        if (!isMobileViewport()) return 16; // desktop ~60fps
        if (isSirenActive || isSpinning) return 50; // mobile active states ~20fps
        return 140; // mobile idle ~7fps
    }

    function canRenderLightFrame()
    {
        if (document.hidden) return false;
        if (!slotIsVisible) return false;
        return true;
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
        const selectors = [
            '#hero-section .mobile-sequence-gated',
            '#hero-section .hero-slot-shell'
        ];

        selectors.forEach((selector) =>
        {
            document.querySelectorAll(selector).forEach((element) => targets.add(element));
        });

        return Array.from(targets);
    }

    function setGatedVisibility(isVisible)
    {
        const heroSection = document.getElementById('hero-section');
        if (!heroSection) return;

        // Keep reveal class/CSS driven to avoid stale inline hidden states on mobile.
        const copy = heroSection.querySelector('.hero-copy');
        if (copy)
        {
            copy.style.opacity = '';
            copy.style.pointerEvents = '';
        }

        const gatedElements = getGateTargets();
        gatedElements.forEach((element) =>
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
        setGatedVisibility(false);
    }

    function initEarlyMobileGate()
    {
        if (earlyGateObserver) return;

        applyEarlyMobileGate();

        const appRoot = document.getElementById('app') || document.body;
        if (!appRoot) return;

        earlyGateObserver = new MutationObserver(() =>
        {
            applyEarlyMobileGate();
        });

        earlyGateObserver.observe(appRoot, { childList: true, subtree: true });
    }

    function onHeroSwipeTouchStart(e)
    {
        if (!isMobileViewport()) return;
        if (!e || !e.touches || e.touches.length !== 1) return;
        const layout = document.querySelector('#hero-section .hero-layout');
        if (!layout) return;
        if (!layout.contains(e.target)) return;

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
        const isSwipeLeft = dx < 0;
        let targetLeft = currentLeft;

        if (isSwipeLeft && currentLeft < maxLeft - 8)
        {
            targetLeft = maxLeft;
        } else if (!isSwipeLeft && currentLeft > 8)
        {
            targetLeft = 0;
        }

        if (Math.abs(targetLeft - currentLeft) < 1) return false;

        layout.scrollTo({
            left: targetLeft,
            behavior: 'smooth'
        });
        return true;
    }

    function onHeroSwipeTouchMove(e)
    {
        if (!heroTouchTracking || !isMobileViewport()) return;
        if (!e || !e.touches || e.touches.length === 0) return;

        // Once a swipe navigation has been triggered, keep suppressing
        // native horizontal scrolling until touchend to avoid in/out jitter.
        if (heroTouchHandled)
        {
            if (e.cancelable) e.preventDefault();
            return;
        }

        const moveTouch = e.touches[0];
        const dx = moveTouch.clientX - heroTouchStartX;
        const dy = moveTouch.clientY - heroTouchStartY;

        // Defer until we are sure this is a horizontal gesture.
        if (Math.abs(dx) < 28) return;
        if (Math.abs(dx) <= Math.abs(dy) * 1.05) return;

        const layout = heroSwipeLayout || document.querySelector('#hero-section .hero-layout');
        if (!layout) return;

        if (e.cancelable) e.preventDefault();

        if (heroTouchHandled) return;
        heroTouchHandled = navigateHeroBySwipe(layout, dx);
        if (heroTouchHandled)
        {
            // Keep tracking until touchend so we can continue preventing
            // native scroll on this gesture.
            heroSwipeLayout = layout;
        }
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

        // Require a deliberate horizontal swipe; do not hijack vertical scrolling.
        if (Math.abs(dx) < 42) return;
        if (Math.abs(dx) < Math.abs(dy) * 1.1) return;

        const layout = heroSwipeLayout || document.querySelector('#hero-section .hero-layout');
        heroSwipeLayout = null;
        if (!layout) return;

        navigateHeroBySwipe(layout, dx);
    }

    function setupHeroSwipeNavigation()
    {
        if (heroSwipeBound) return;

        // Capture at document level so swipe back from the slot panel cannot be swallowed by nested touch handlers.
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

    function updateCreditDisplay()
    {
        const display = document.getElementById('credit-display');
        if (display)
        {
            display.textContent = String(currentCredits).padStart(2, '0');
        }
    }

    function triggerCreditAlarm()
    {
        if (isAlarmActive) return;
        isAlarmActive = true;

        const counter = document.querySelector('.credit-counter');
        if (counter)
        {
            counter.classList.add('credit-alarm');
            // Remove alarm after 3 seconds
            setTimeout(() =>
            {
                // Only clear if still active (didn't insert coin)
                if (isAlarmActive)
                {
                    counter.classList.remove('credit-alarm');
                    isAlarmActive = false;
                }
            }, 3000);
        }
    }

    function insertCoin()
    {
        // Cancel alarm if active
        if (isAlarmActive)
        {
            isAlarmActive = false;
            const counter = document.querySelector('.credit-counter');
            if (counter) counter.classList.remove('credit-alarm');
        }

        const container = document.querySelector('.coin-insert');
        if (!container)
        {
            console.error("SlotMachine: .coin-insert container not found");
            return;
        }

        // Calculate Position for Portal
        const rect = container.getBoundingClientRect();
        const scrollX = window.scrollX || window.pageXOffset;
        const scrollY = window.scrollY || window.pageYOffset;

        // Target offsets relative to container (from previous CSS)
        // CSS was: top: -20px, right: -4px
        // width: 24px
        // rect.right is the right edge.
        // We place the 24px coin such that its right edge is at rect.right - (-4) = rect.right + 4?
        // "right: -4px" means pushed outside. Position = rect.right + 4 - width.
        const coinW = 24;
        const left = (rect.right + 4 - coinW) + scrollX;
        const top = (rect.top - 20) + scrollY;

        // Create Scene (Perspective Container)
        const scene = document.createElement('div');
        scene.style.position = 'absolute';
        scene.style.left = `${left}px`;
        scene.style.top = `${top}px`;
        scene.style.width = `${coinW}px`;
        scene.style.height = '24px';
        scene.style.zIndex = '99999';
        scene.style.pointerEvents = 'none';
        scene.style.perspective = '2000px'; // Fixes distortion

        // Create 3D Coin Wrapper
        const coinWrapper = document.createElement('div');
        coinWrapper.className = 'coin-wrapper anim-insert';
        // Reset positioning relative to scene
        coinWrapper.style.position = 'absolute';
        coinWrapper.style.top = '0';
        coinWrapper.style.left = '0';
        coinWrapper.style.right = 'auto';

        // Create Layers (Heads/Tails Logic)
        const isHeads = Math.random() > 0.5;

        for (let i = -2; i <= 2; i++)
        {
            const layer = document.createElement('div');
            layer.className = 'coin-layer';

            let transform = `translateZ(${i * 4}px)`;
            if (i === -2) transform += ' rotateY(180deg)';
            layer.style.transform = transform;

            if (Math.abs(i) === 2)
            {
                layer.classList.add('coin-face');
                if (i === 2) layer.classList.add(isHeads ? 'coin-face-front' : 'coin-face-back');
                if (i === -2) layer.classList.add(isHeads ? 'coin-face-back' : 'coin-face-front');
            } else
            {
                layer.classList.add('coin-edge');
            }

            coinWrapper.appendChild(layer);
        }

        scene.appendChild(coinWrapper);
        document.body.appendChild(scene);

        // Cleanup
        setTimeout(() =>
        {
            if (scene.parentNode) scene.parentNode.removeChild(scene);
            currentCredits++;
            updateCreditDisplay();
        }, 1500);
    }

    // Casino Lights Generator
    function initLights()
    {
        const list = document.getElementById('casino-lights');
        const wrapper = document.querySelector('.slot-machine-wrapper');
        if (!list || !wrapper) return;

        // Stop existing animation loop to prevent duplicates/memory leaks
        if (lightAnimationId) cancelAnimationFrame(lightAnimationId);
        lightLastFrameTime = 0;

        list.innerHTML = '';

        // Use offsetWidth/Height instead of getBoundingClientRect 
        // to ignore CSS transforms (scale) on mobile.
        const w = wrapper.offsetWidth;
        const h = wrapper.offsetHeight;

        const rx = w / 2;
        const ry = 100;
        const spacing = 35; // Pixel spacing between bulbs

        // Temporary arrays to hold bulbs before combining
        const arcBulbs = [];
        const leftBulbs = [];
        const rightBulbs = [];

        // Shift logic for right side (Move tiny bit to the left)
        const getShiftedX = (x) =>
        {
            if (x <= rx) return x;
            // Smoothly increase shift from 0 at center peak to 4px at the right edge
            const factor = (x - rx) / rx;
            return x - (factor * 4);
        };

        // Helper to create a 3D bulb element (does not append to DOM yet)
        const createBulb = (x, y, rot) =>
        {
            const li = document.createElement('li');
            li.style.left = `${getShiftedX(x)}px`;
            li.style.top = `${y}px`;
            li.style.transform = `rotate(${rot}deg)`;
            return li;
        };

        const getPoint = (t) => ({
            x: rx + rx * Math.cos(t),
            y: ry + ry * Math.sin(t)
        });

        // Calculate Total Arc Length
        let totalArcLength = 0;
        const segments = 300;
        const dt = Math.PI / segments;
        let prevP = getPoint(Math.PI);

        for (let i = 1; i <= segments; i++)
        {
            const t = Math.PI + (i * dt);
            const p = getPoint(t);
            totalArcLength += Math.sqrt(Math.pow(p.x - prevP.x, 2) + Math.pow(p.y - prevP.y, 2));
            prevP = p;
        }

        const numIntervals = Math.round(totalArcLength / spacing);
        const actualSpacing = totalArcLength / numIntervals;

        // --- 1. ARC BULBS GENERATION ---
        let currentLen = 0;
        let nextTarget = 0;

        prevP = getPoint(Math.PI); // Reset

        // Place first bulb manually (Left Corner of Arc)
        arcBulbs.push(createBulb(prevP.x, prevP.y, 270));
        nextTarget += actualSpacing;

        for (let i = 1; i <= segments; i++)
        {
            const t = Math.PI + (i * dt);
            const p = getPoint(t);
            const dist = Math.sqrt(Math.pow(p.x - prevP.x, 2) + Math.pow(p.y - prevP.y, 2));

            if (currentLen + dist >= nextTarget)
            {
                // Interpolate for precision
                const remaining = nextTarget - currentLen;
                const ratio = remaining / dist;
                const interpT = (Math.PI + ((i - 1) * dt)) + (ratio * dt);
                const finalP = getPoint(interpT);
                const deg = (interpT * 180 / Math.PI) + 90;

                arcBulbs.push(createBulb(finalP.x, finalP.y, deg));
                nextTarget += actualSpacing;
            }
            currentLen += dist;
            prevP = p;
        }

        // --- 2. SIDE LIGHTS GENERATION ---
        const sideStartY = ry;
        const sideEndY = h - 35; // Shorten (approx 1 bulb)
        const numSide = Math.max(0, Math.floor((sideEndY - sideStartY) / spacing));

        for (let i = 0; i < numSide; i++)
        {
            const y = sideStartY + ((i + 1) * spacing);
            // Left Edge (x = 0) - Bottom to Top requires reverse logic later
            leftBulbs.push(createBulb(0, y, 0));
            // Right Edge (x = w) - Top to Bottom
            rightBulbs.push(createBulb(w, y, 180));
        }

        // --- 3. COMBINE & INDEX ---
        // Sequence: Left (Bottom->Top) -> Arc (Left->Right) -> Right (Top->Bottom)
        // leftBulbs was generated Top->Bottom (y increasing), so we reverse it.
        const bulbs = [...leftBulbs.reverse(), ...arcBulbs, ...rightBulbs];

        bulbs.forEach((li, i) =>
        {
            // Store metadata for the animation loop
            li.dataset.group = i % 6;
            li.dataset.index = i;
            list.appendChild(li);
        });

        // Single Animation Loop for High Performance
        const animate = (timestamp = 0) =>
        {
            if (!canRenderLightFrame())
            {
                lightAnimationId = requestAnimationFrame(animate);
                return;
            }

            const frameInterval = getLightFrameInterval();
            if (lightLastFrameTime !== 0 && (timestamp - lightLastFrameTime) < frameInterval)
            {
                lightAnimationId = requestAnimationFrame(animate);
                return;
            }
            lightLastFrameTime = timestamp;

            const now = Date.now();

            if (isSirenActive)
            {
                // Synchronous fast blinking (Panic Mode - RED)
                const isPhaseOn = Math.floor(now / 300) % 2 === 0;
                for (let i = 0; i < bulbs.length; i++)
                {
                    const li = bulbs[i];
                    li.classList.add('bulb-red'); // Turn RED
                    if (isPhaseOn) li.classList.remove('bulb-off');
                    else li.classList.add('bulb-off');
                }
            } else if (isSpinning)
            {
                // Sequential Chase (Running Light)
                const speed = 20; // Lower is faster
                const totalBulbs = bulbs.length;
                const activeIndex = Math.floor(now / speed) % totalBulbs;

                for (let i = 0; i < totalBulbs; i++)
                {
                    const li = bulbs[i];
                    li.classList.remove('bulb-red'); // Ensure not red
                    // Simple dot mode
                    if (i === activeIndex) li.classList.remove('bulb-off');
                    else li.classList.add('bulb-off');
                }
            } else
            {
                // Idle / Attract Mode (Original Logic)
                // Logic: 1200ms ON, 1200ms OFF => 2400ms period.
                // Stagger offset: group * 400ms.
                for (let i = 0; i < bulbs.length; i++)
                {
                    const li = bulbs[i];
                    li.classList.remove('bulb-red'); // Ensure not red
                    const group = parseInt(li.dataset.group);
                    const index = parseInt(li.dataset.index);

                    const offset = group * 400;
                    const tEff = now - offset;
                    const phase = Math.floor(tEff / 1200);

                    // Initial state logic from original: i % 2 === 0 starts OFF
                    const initialIsOff = (index % 2 === 0);
                    let isOff = initialIsOff;

                    // Flip state every 1200ms phase
                    if (phase % 2 !== 0)
                    {
                        isOff = !isOff;
                    }

                    if (isOff)
                    {
                        li.classList.add('bulb-off');
                    } else
                    {
                        li.classList.remove('bulb-off');
                    }
                }
            }

            lightAnimationId = requestAnimationFrame(animate);
        };

        // Start loop
        animate();
    }

    function initReels()
    {
        const reels = [
            document.getElementById('reel-1'),
            document.getElementById('reel-2'),
            document.getElementById('reel-3')
        ];

        const START_WORDS = ["WORLD-CLASS", "ECONOMIC ENGINE", "HUMANITARIAN FUND"];

        reels.forEach((reel, index) =>
        {
            if (!reel) return;

            // Mobile performance: hint GPU to prepare for transforms
            reel.style.willChange = 'transform';

            let content = "";

            for (let i = 0; i < SLOT_COUNT; i++)
            {
                let word;
                let className = "slot-item";

                if (i === 0)
                {
                    word = START_WORDS[index];
                } else
                {
                    if (Math.random() > 0.33)
                    {
                        word = BAIT_WORDS[Math.floor(Math.random() * BAIT_WORDS.length)];
                    } else
                    {
                        word = TARGET_WORDS[Math.floor(Math.random() * TARGET_WORDS.length)];
                        className += " truth-word";
                    }
                }

                const theta = 360 / SLOT_COUNT;
                const angle = theta * i;

                content += `<div class="${className}" style="transform: rotateX(${angle}deg) translateZ(${RADIUS}px)">` + word + `</div>`;
            }

            reel.style.transition = 'none';
            reel.innerHTML = content;
            reel.style.transform = `translateZ(-${RADIUS}px) rotateX(0deg)`;
            reel.offsetHeight;
            reel.style.transition = '';

            fitReelSlots(reel);
        });
    }

    function fitSlotText(slot)
    {
        if (!slot) return;

        const mobile = isMobileViewport();
        const preferredFontSize = mobile ? 12 : 16;
        const minFontSize = mobile ? 9.5 : 13.5;
        const widthPadding = 8;
        const heightPadding = 6;

        slot.style.fontSize = `${preferredFontSize}px`;
        slot.style.lineHeight = '1.08';
        slot.style.wordBreak = 'normal';
        slot.style.overflowWrap = 'normal';
        slot.style.hyphens = 'none';

        let fontSize = preferredFontSize;
        let guard = 0;

        while (
            (slot.scrollWidth > (slot.clientWidth - widthPadding) || slot.scrollHeight > (slot.clientHeight - heightPadding)) &&
            fontSize > minFontSize &&
            guard < 24
        )
        {
            fontSize -= 0.5;
            slot.style.fontSize = `${fontSize}px`;
            guard++;
        }
    }

    function fitReelSlots(reel)
    {
        if (!reel) return;
        for (const child of reel.children)
        {
            if (child && child.classList && child.classList.contains('slot-item'))
            {
                fitSlotText(child);
            }
        }
    }

    function fitAllReelSlots()
    {
        fitReelSlots(document.getElementById('reel-1'));
        fitReelSlots(document.getElementById('reel-2'));
        fitReelSlots(document.getElementById('reel-3'));
    }

    function updateSlot(reelEl, index, text, type)
    {
        const safeIndex = (index + SLOT_COUNT) % SLOT_COUNT;
        const slot = reelEl.children[safeIndex];

        slot.textContent = text;
        slot.className = 'slot-item';

        if (type === 'truth')
        {
            slot.classList.add('truth-word');
        } else if (type === 'near-miss')
        {
            slot.classList.add('near-miss');
        }

        fitSlotText(slot);
    }

    function performSpin(index, delay = 0)
    {
        const reel = document.getElementById(`reel-${index + 1}`);
        if (!reel) return;

        const theta = 360 / SLOT_COUNT;

        const targetIndex = isAtFront ? 8 : 0;
        const destAngle = -1 * targetIndex * theta;

        const negativeWord = TARGET_WORDS[Math.floor(Math.random() * TARGET_WORDS.length)];
        updateSlot(reel, targetIndex, negativeWord, 'truth');

        const shuffledBait = [...BAIT_WORDS].sort(() => 0.5 - Math.random());
        const near1 = shuffledBait[0];
        const near2 = shuffledBait[1];
        updateSlot(reel, targetIndex - 1, near1, 'near-miss');
        updateSlot(reel, targetIndex + 1, near2, 'near-miss');

        let current = currentRotation[index];
        let currentNormalized = current % 360;
        let diff = destAngle - currentNormalized;
        while (diff < 0) diff += 360;

        let spin = 360 * 5;
        let finalRotation = current + spin + diff;

        currentRotation[index] = finalRotation;

        setTimeout(() =>
        {
            reel.style.transform = `translateZ(-${RADIUS}px) rotateX(${finalRotation}deg)`;
        }, delay);
    }

    function spinReel(index)
    {
        if (isSpinning) return;

        const reelIndex = Number(index);
        if (isMobileViewport() && reelIndex === 1)
        {
            isSpinning = true;
            resetSiren();
            isAtFront = !isAtFront;

            [0, 1, 2].forEach((i) =>
            {
                performSpin(i, i * 200);
            });

            setTimeout(() =>
            {
                isSpinning = false;
                triggerSirenSequence();
            }, 6500);
            return;
        }

        performSpin(index, 0);
    }

    function resetSiren()
    {
        isSirenActive = false;
        const siren = document.getElementById('siren');
        if (siren) siren.classList.remove('siren-active');
        if (sirenTimeout) clearTimeout(sirenTimeout);
    }

    function triggerSirenSequence()
    {
        const siren = document.getElementById('siren');
        if (!siren) return;

        siren.classList.add('siren-active');
        isSirenActive = true;

        if (sirenTimeout) clearTimeout(sirenTimeout);
        sirenTimeout = setTimeout(() =>
        {
            siren.classList.remove('siren-active');
            isSirenActive = false;
        }, 5000);
    }

    function spinTheTruth()
    {
        // Credit Check
        if (currentCredits <= 0)
        {
            triggerCreditAlarm();
            return;
        }

        if (isSpinning) return;

        // Decrement Credit
        currentCredits--;
        updateCreditDisplay();

        isSpinning = true;

        resetSiren();

        isAtFront = !isAtFront;
        const targetIndex = isAtFront ? 8 : 0;

        [0, 1, 2].forEach(index =>
        {
            performSpin(index, index * 200);
        });

        setTimeout(() =>
        {
            isSpinning = false;
            triggerSirenSequence();
        }, 6500);
    }

    function initLever()
    {
        const lever = document.getElementById('slot-lever');
        const knob = document.querySelector('.lever-knob');

        if (!lever || !knob) return;

        let isDragging = false;
        let startY = 0;
        let dragMoved = false;
        let suppressClickUntil = 0;

        function startDrag(e)
        {
            if (isSpinning) return;
            if (e.cancelable) e.preventDefault();

            isDragging = true;
            dragMoved = false;
            startY = e.type === 'touchstart' ? e.touches[0].clientY : e.clientY;
            lever.style.transition = 'none';
            knob.style.transition = 'none';

            // Mobile performance: hint GPU
            lever.style.willChange = 'transform';
            knob.style.willChange = 'transform';
        }

        function moveDrag(e)
        {
            if (!isDragging) return;
            if (e.cancelable) e.preventDefault();

            const currentY = e.type === 'touchmove' ? e.touches[0].clientY : e.clientY;
            const diff = currentY - startY;
            if (Math.abs(diff) > 3) dragMoved = true;

            let deg = Math.min(70, Math.max(0, diff / 2.5));

            lever.style.transform = `rotateX(-${deg}deg)`;
            knob.style.transform = `translateX(-50%) translateZ(10px) rotateX(${deg}deg)`;
        }

        function endDrag(e)
        {
            if (!isDragging) return;
            isDragging = false;

            const transform = lever.style.transform;
            const match = transform.match(/rotateX\(([-\d.]+)deg\)/);
            const currentDeg = match ? parseFloat(match[1]) : 0;

            lever.style.transition = 'transform 0.4s cubic-bezier(0.175, 0.885, 0.32, 1.275)';
            knob.style.transition = 'transform 0.4s cubic-bezier(0.175, 0.885, 0.32, 1.275)';

            lever.style.transform = `rotateX(0deg)`;
            knob.style.transform = `translateX(-50%) translateZ(10px) rotateX(0deg)`;

            // Remove will-change after transition completes
            setTimeout(() =>
            {
                lever.style.willChange = 'auto';
                knob.style.willChange = 'auto';
            }, 450);

            if (Math.abs(currentDeg) > 45)
            {
                suppressClickUntil = Date.now() + 500;
                spinTheTruth();
            } else if (!dragMoved && Math.abs(currentDeg) < 10)
            {
                // Treat tap/click on the knob as a full pull action.
                suppressClickUntil = Date.now() + 500;
                animateLeverPull();
            }

            dragMoved = false;
        }

        function animateLeverPull()
        {
            if (isSpinning) return;

            lever.classList.add('lever-pulled');
            spinTheTruth();

            setTimeout(() =>
            {
                lever.classList.remove('lever-pulled');
            }, 600);
        }

        function onLeverClick(e)
        {
            if (isSpinning || isDragging) return;
            if (Date.now() < suppressClickUntil) return;
            if (dragMoved)
            {
                dragMoved = false;
                return;
            }
            if (e && e.cancelable) e.preventDefault();
            animateLeverPull();
        }

        function onLeverKeyDown(e)
        {
            if (!e) return;
            const key = String(e.key || '').toLowerCase();
            if (key === 'enter' || key === ' ')
            {
                if (e.cancelable) e.preventDefault();
                onLeverClick(e);
            }
        }

        lever.addEventListener('mousedown', startDrag);
        document.addEventListener('mousemove', moveDrag);
        document.addEventListener('mouseup', endDrag);

        lever.addEventListener('touchstart', startDrag, { passive: false });
        document.addEventListener('touchmove', moveDrag, { passive: false });
        document.addEventListener('touchend', endDrag);
        lever.addEventListener('click', onLeverClick);
        lever.addEventListener('keydown', onLeverKeyDown);
    }

    function setMobileSequenceState(state)
    {
        const heroSection = document.getElementById('hero-section');
        if (!heroSection) return;

        heroSection.classList.remove('mobile-sequence-pending');
        heroSection.classList.remove('mobile-sequence-ready');

        if (state === 'pending')
        {
            mobileSequenceReady = false;
            heroSection.classList.add('mobile-sequence-pending');
            setGatedVisibility(false);
            return;
        }

        mobileSequenceReady = true;
        heroSection.classList.add('mobile-sequence-ready');
        setGatedVisibility(true);
    }

    function initializeMachine()
    {
        if (isInitialized) return;
        isInitialized = true;

        initSlotVisibilityTracking();
        initReels();
        initLights();
        initLever();
        updateCreditDisplay();

        const wrapper = document.querySelector('.slot-machine-wrapper');
        if (wrapper && !resizeObserver)
        {
            resizeObserver = new ResizeObserver(entries =>
            {
                // Debounce re-layout
                clearTimeout(window._slotResizeTimer);
                window._slotResizeTimer = setTimeout(() =>
                {
                    initLights();
                    fitAllReelSlots();
                }, 50);
            });
            resizeObserver.observe(wrapper);
        }

        // Expose spinReel globally if needed by buttons outside
        window.spinReel = spinReel;
    }

    function init(optionsOrMobileMaxWidth, mobileRevealDelayMsArg)
    {
        let mobileMaxWidth = 1023;
        let mobileRevealDelayMs = 6200;

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
        const isMobile = isMobileViewport(mobileMaxWidth);
        mobileSequenceReady = false;

        setupHeroSwipeNavigation();
        applyEarlyMobileGate();

        if (deferredInitTimer)
        {
            clearTimeout(deferredInitTimer);
            deferredInitTimer = null;
        }

        if (!isMobile)
        {
            setMobileSequenceState('ready');
            initializeMachine();
            return;
        }

        setMobileSequenceState('pending');

        deferredInitTimer = setTimeout(() =>
        {
            try
            {
                initializeMachine();
            } catch (error)
            {
                console.error('SlotMachine initialization failed; forcing mobile ready state.', error);
            } finally
            {
                setMobileSequenceState('ready');
                deferredInitTimer = null;
            }
        }, Math.max(0, mobileRevealDelayMs));
    }

    initEarlyMobileGate();

    return {
        init: init,
        insertCoin: insertCoin
    };
})();
