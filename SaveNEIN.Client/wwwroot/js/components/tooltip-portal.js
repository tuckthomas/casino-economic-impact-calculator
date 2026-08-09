/**
 * Canonical SaveNEIN tooltip portal.
 * App-owned Blazor UI should consume AppTooltip.razor. Controls created dynamically
 * by JavaScript use window.AppTooltip.attach so both paths share identical behavior.
 */
window.TooltipPortal = (function ()
{
    'use strict';

    const bindings = new WeakMap();
    let portalEl = null;
    let arrowEl = null;
    let hideTimeout = null;
    let currentTrigger = null;
    let describedElement = null;
    let previousDescribedBy = null;

    function ensurePortal()
    {
        if (portalEl) return portalEl;

        portalEl = document.createElement('div');
        portalEl.id = 'app-tooltip-portal';
        portalEl.setAttribute('role', 'tooltip');
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
        arrowEl.setAttribute('aria-hidden', 'true');
        arrowEl.style.cssText = `
  position: absolute;
  width: 0;
  height: 0;
  border-left: 6px solid transparent;
  border-right: 6px solid transparent;
        `;
        portalEl.appendChild(arrowEl);

        const textEl = document.createElement('span');
        textEl.className = 'tooltip-portal-text';
        portalEl.appendChild(textEl);
        document.body.appendChild(portalEl);

        window.addEventListener('scroll', hide, true);
        window.addEventListener('resize', hide, { passive: true });
        return portalEl;
    }

    function clearDescription()
    {
        if (!describedElement) return;
        if (previousDescribedBy)
        {
  describedElement.setAttribute('aria-describedby', previousDescribedBy);
        }
        else if (describedElement.getAttribute('aria-describedby') === 'app-tooltip-portal')
        {
  describedElement.removeAttribute('aria-describedby');
        }
        describedElement = null;
        previousDescribedBy = null;
    }

    function positionTooltip(triggerEl)
    {
        if (!portalEl || !triggerEl) return;
        const rect = triggerEl.getBoundingClientRect();
        const portalRect = portalEl.getBoundingClientRect();
        const gap = 8;
        let top = rect.top - portalRect.height - gap;
        let placeBelow = false;
        if (top < 4)
        {
  top = rect.bottom + gap;
  placeBelow = true;
        }

        let left = rect.left + rect.width / 2 - portalRect.width / 2;
        const viewportWidth = window.innerWidth;
        if (left < 8) left = 8;
        if (left + portalRect.width > viewportWidth - 8) left = viewportWidth - portalRect.width - 8;

        portalEl.style.top = `${top}px`;
        portalEl.style.left = `${left}px`;

        if (placeBelow)
        {
  arrowEl.style.top = '-6px';
  arrowEl.style.bottom = '';
  arrowEl.style.borderBottom = '6px solid #0f172a';
  arrowEl.style.borderTop = 'none';
        }
        else
        {
  arrowEl.style.bottom = '-6px';
  arrowEl.style.top = '';
  arrowEl.style.borderTop = '6px solid #0f172a';
  arrowEl.style.borderBottom = 'none';
        }

        const arrowLeft = rect.left + rect.width / 2 - left - 6;
        arrowEl.style.left = `${Math.max(6, Math.min(arrowLeft, portalRect.width - 18))}px`;
    }

    function show(triggerEl, content, accessibilityTarget)
    {
        if (!triggerEl || !content) return;
        clearTimeout(hideTimeout);
        ensurePortal();
        clearDescription();

        currentTrigger = triggerEl;
        const textEl = portalEl.querySelector('.tooltip-portal-text');
        textEl.textContent = String(content);

        describedElement = accessibilityTarget instanceof Element ? accessibilityTarget : triggerEl;
        previousDescribedBy = describedElement.getAttribute('aria-describedby');
        const ids = new Set((previousDescribedBy || '').split(/\s+/).filter(Boolean));
        ids.add(portalEl.id);
        describedElement.setAttribute('aria-describedby', Array.from(ids).join(' '));

        portalEl.style.opacity = '0';
        portalEl.style.display = 'block';
        requestAnimationFrame(() =>
        {
  if (currentTrigger !== triggerEl) return;
  positionTooltip(triggerEl);
  portalEl.style.opacity = '1';
        });
    }

    function hide()
    {
        if (!portalEl) return;
        clearTimeout(hideTimeout);
        hideTimeout = setTimeout(() =>
        {
  portalEl.style.opacity = '0';
  currentTrigger = null;
  clearDescription();
        }, 50);
    }

    function detach(triggerEl)
    {
        if (!triggerEl) return;
        const binding = bindings.get(triggerEl);
        if (binding)
        {
  for (const [eventName, handler] of binding.handlers)
  {
      triggerEl.removeEventListener(eventName, handler);
  }
  bindings.delete(triggerEl);
        }
        triggerEl.removeAttribute('data-app-tooltip-bound');
        if (currentTrigger === triggerEl) hide();
    }

    function attach(triggerEl, content)
    {
        if (!triggerEl || !content) return;
        detach(triggerEl);
        const tooltipText = String(content);
        const handlers = [];
        const on = (eventName, handler) =>
        {
  triggerEl.addEventListener(eventName, handler);
  handlers.push([eventName, handler]);
        };

        on('mouseenter', () => show(triggerEl, tooltipText, triggerEl));
        on('mouseleave', hide);
        on('focusin', event => show(triggerEl, tooltipText, event.target));
        on('focusout', event =>
        {
  if (!event.relatedTarget || !triggerEl.contains(event.relatedTarget)) hide();
        });
        on('keydown', event =>
        {
  if (event.key === 'Escape') hide();
        });
        on('pointerdown', event =>
        {
  if (event.pointerType === 'touch')
  {
      show(triggerEl, tooltipText, event.target);
      window.setTimeout(hide, 2200);
  }
        });

        bindings.set(triggerEl, { handlers });
        triggerEl.setAttribute('data-app-tooltip-bound', 'true');
    }

    function attachById(id, content)
    {
        const element = document.getElementById(id);
        if (element) attach(element, content);
    }

    function detachById(id)
    {
        const element = document.getElementById(id);
        if (element) detach(element);
    }

    const portalApi = { show, hide, attach, detach, attachById, detachById };
    window.AppTooltip = { attach, detach, attachById, detachById, hide };
    return portalApi;
})();
