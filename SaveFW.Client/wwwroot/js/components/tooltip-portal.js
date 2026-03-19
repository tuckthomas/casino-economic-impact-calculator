/**
 * TooltipPortal - Body-level tooltip rendering to escape stacking context issues.
 * 
 * Renders tooltip content in a single <div> appended to <body>, completely outside
 * any component's DOM hierarchy. This eliminates z-index stacking context problems
 * where tooltips inside lower z-index parents get clipped behind sibling elements.
 *
 * @module TooltipPortal
 */
window.TooltipPortal = (function ()
{
    'use strict';

    let portalEl = null;
    let arrowEl = null;
    let hideTimeout = null;
    let currentTrigger = null;

    function ensurePortal()
    {
        if (portalEl) return;

        portalEl = document.createElement('div');
        portalEl.id = 'tooltip-portal';
        portalEl.style.cssText = `
            position: fixed;
            z-index: 99999;
            pointer-events: none;
            opacity: 0;
            transition: opacity 0.15s ease;
            max-width: 220px;
            padding: 8px 12px;
            background: #0f172a;
            color: #f1f5f9;
            font-size: 12px;
            line-height: 1.4;
            border-radius: 8px;
            border: 1px solid #334155;
            box-shadow: 0 10px 25px rgba(0,0,0,0.4), 0 4px 10px rgba(0,0,0,0.3);
            text-align: center;
            font-weight: 400;
            text-transform: none;
            letter-spacing: normal;
            white-space: normal;
            word-wrap: break-word;
        `;

        arrowEl = document.createElement('div');
        arrowEl.style.cssText = `
            position: absolute;
            width: 0;
            height: 0;
            border-left: 6px solid transparent;
            border-right: 6px solid transparent;
        `;
        portalEl.appendChild(arrowEl);

        document.body.appendChild(portalEl);
    }

    function positionTooltip(triggerEl)
    {
        if (!portalEl || !triggerEl) return;

        const rect = triggerEl.getBoundingClientRect();
        const portalRect = portalEl.getBoundingClientRect();
        const gap = 8;

        // Default: position above
        let top = rect.top - portalRect.height - gap;
        let placeBelow = false;

        // If no room above, flip below
        if (top < 4)
        {
            top = rect.bottom + gap;
            placeBelow = true;
        }

        // Center horizontally on trigger
        let left = rect.left + rect.width / 2 - portalRect.width / 2;

        // Clamp to viewport edges
        const vw = window.innerWidth;
        if (left < 8) left = 8;
        if (left + portalRect.width > vw - 8) left = vw - portalRect.width - 8;

        portalEl.style.top = top + 'px';
        portalEl.style.left = left + 'px';

        // Arrow
        if (placeBelow)
        {
            arrowEl.style.top = '-6px';
            arrowEl.style.bottom = '';
            arrowEl.style.borderBottom = '6px solid #0f172a';
            arrowEl.style.borderTop = 'none';
        } else
        {
            arrowEl.style.bottom = '-6px';
            arrowEl.style.top = '';
            arrowEl.style.borderTop = '6px solid #0f172a';
            arrowEl.style.borderBottom = 'none';
        }

        // Center arrow on trigger's midpoint relative to portal
        const arrowLeft = rect.left + rect.width / 2 - left - 6;
        arrowEl.style.left = Math.max(6, Math.min(arrowLeft, portalRect.width - 18)) + 'px';
    }

    function show(triggerEl, content)
    {
        if (!triggerEl || !content) return;

        clearTimeout(hideTimeout);
        ensurePortal();

        currentTrigger = triggerEl;

        // Set content (keep arrow as first child)
        // textContent for the portal body, arrow is the first child
        // We need to handle HTML content, so set innerHTML but keep arrow
        const textNode = portalEl.querySelector('.tooltip-portal-text');
        if (textNode)
        {
            textNode.textContent = content;
        } else
        {
            const span = document.createElement('span');
            span.className = 'tooltip-portal-text';
            span.textContent = content;
            portalEl.appendChild(span);
        }

        // Make visible but transparent to measure
        portalEl.style.opacity = '0';
        portalEl.style.display = 'block';

        // Position after layout
        requestAnimationFrame(() =>
        {
            positionTooltip(triggerEl);
            portalEl.style.opacity = '1';
        });
    }

    function hide()
    {
        if (!portalEl) return;

        hideTimeout = setTimeout(() =>
        {
            portalEl.style.opacity = '0';
            currentTrigger = null;
        }, 50);
    }

    /**
     * Attach tooltip behavior to a trigger element.
     * @param {HTMLElement} triggerEl - The element that triggers the tooltip on hover
     * @param {string} content - The tooltip text content
     */
    function attach(triggerEl, content)
    {
        if (!triggerEl || !content) return;

        triggerEl.addEventListener('mouseenter', () => show(triggerEl, content));
        triggerEl.addEventListener('mouseleave', () => hide());
        triggerEl.addEventListener('focusin', () => show(triggerEl, content));
        triggerEl.addEventListener('focusout', () => hide());
    }

    return { show, hide, attach };
})();
