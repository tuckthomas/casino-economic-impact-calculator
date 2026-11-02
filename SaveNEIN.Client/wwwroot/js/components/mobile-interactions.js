import { WebHaptics } from '../../lib/web-haptics/index.mjs?v=0.0.6';

// Enhance the existing native select/change contract; Blazor keeps ownership of its DOM.
const compact = window.matchMedia('(max-width: 767px)');
const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
let activePicker = null;
let haptics = null;

function feedback(event, pattern = 'selection') {
    if (!event.isTrusted || event.detail === 0 || event.pointerType === 'mouse' || document.hidden || reducedMotion.matches ||
        !window.matchMedia('(any-pointer: coarse)').matches ||
        event.target.closest?.('[data-haptics="off"]')) return;
    try {
        haptics ??= new WebHaptics({ debug: false, showSwitch: false });
        haptics.trigger(pattern).catch(() => {});
    } catch { /* Feedback is progressive enhancement, never a prerequisite for an action. */ }
}

function closePicker() {
    if (!activePicker) return;
    const { dialog, trigger, previousOverflow, previousHasPopup } = activePicker;
    activePicker = null;
    dialog.close();
    dialog.remove();
    document.body.style.overflow = previousOverflow;
    trigger.setAttribute('aria-expanded', 'false');
    if (previousHasPopup === null) trigger.removeAttribute('aria-haspopup');
    else trigger.setAttribute('aria-haspopup', previousHasPopup);
    if (trigger.isConnected) trigger.focus({ preventScroll: true });
}

function openPicker(trigger, select) {
    closePicker();
    const dialog = document.createElement('dialog');
    dialog.id = 'mobile-selection-sheet';
    dialog.className = 'sfw-selection-sheet';
    dialog.setAttribute('aria-labelledby', 'mobile-selection-title');
    dialog.setAttribute('aria-modal', 'true');

    const header = document.createElement('header');
    const title = document.createElement('h2');
    title.id = 'mobile-selection-title';
    title.textContent = trigger.dataset.mobileLabel || 'Choose an option';
    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'sfw-sheet-close';
    close.textContent = 'Done';
    close.setAttribute('aria-label', 'Close selection');
    close.addEventListener('click', closePicker);
    header.append(title, close);
    dialog.append(header);

    const options = Array.from(select.options);
    const list = document.createElement('div');
    list.className = 'sfw-sheet-options';
    list.setAttribute('role', 'listbox');
    list.setAttribute('aria-labelledby', title.id);
    const buttons = options.map(option => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'sfw-sheet-option';
        button.setAttribute('role', 'option');
        button.setAttribute('aria-selected', String(option.value === select.value));
        button.disabled = option.disabled || option.parentElement?.disabled === true;
        const label = document.createElement('span');
        label.textContent = option.label;
        const check = document.createElement('span');
        check.className = 'material-symbols-outlined';
        check.setAttribute('aria-hidden', 'true');
        check.textContent = option.value === select.value ? 'check' : '';
        button.append(label, check);
        button.addEventListener('click', () => {
            if (!select.isConnected || select.disabled) return closePicker();
            // Close before dispatching: Custom's existing handler focuses the editable input.
            closePicker();
            select.value = option.value;
            select.dispatchEvent(new Event('change', { bubbles: true }));
        });
        list.append(button);
        return button;
    });
    if (options.length > 12) {
        const search = document.createElement('input');
        search.type = 'search';
        search.className = 'sfw-sheet-search';
        search.placeholder = 'Filter options…';
        search.setAttribute('aria-label', 'Filter options');
        const empty = document.createElement('p');
        empty.className = 'sfw-sheet-empty';
        empty.textContent = 'No matching options.';
        empty.setAttribute('role', 'status');
        empty.hidden = true;
        search.addEventListener('input', () => {
            const query = search.value.trim().toLocaleLowerCase();
            for (const button of buttons) button.hidden = !button.textContent.toLocaleLowerCase().includes(query);
            empty.hidden = buttons.some(button => !button.hidden);
        });
        dialog.append(search);
        list.append(empty);
    }
    dialog.append(list);
    dialog.addEventListener('cancel', event => { event.preventDefault(); closePicker(); });
    dialog.addEventListener('click', event => { if (event.target === dialog) {
        const bounds = dialog.getBoundingClientRect();
        if (event.clientX < bounds.left || event.clientX > bounds.right ||
            event.clientY < bounds.top || event.clientY > bounds.bottom) closePicker();
    } });
    list.addEventListener('keydown', event => {
        if (!['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) return;
        event.preventDefault();
        const available = buttons.filter(button => !button.hidden && !button.disabled);
        const index = available.indexOf(document.activeElement);
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? available.length - 1
            : (index + (event.key === 'ArrowDown' ? 1 : -1) + available.length) % available.length;
        available[next]?.focus();
    });

    activePicker = { dialog, trigger, previousOverflow: document.body.style.overflow,
        previousHasPopup: trigger.getAttribute('aria-haspopup') };
    document.body.append(dialog);
    document.body.style.overflow = 'hidden';
    trigger.setAttribute('aria-expanded', 'true');
    trigger.setAttribute('aria-haspopup', 'dialog');
    dialog.showModal();
    const selected = buttons.find(button => button.getAttribute('aria-selected') === 'true' && !button.disabled);
    (selected || close).focus({ preventScroll: true });
    selected?.scrollIntoView({ block: 'nearest' });
}

document.addEventListener('click', event => {
    const trigger = event.target.closest?.('[data-mobile-select]');
    if (!compact.matches || !trigger || trigger.disabled) return;
    const select = document.getElementById(trigger.dataset.mobileSelect);
    if (!select || select.disabled || !select.options.length || typeof HTMLDialogElement === 'undefined') return;
    event.preventDefault();
    event.stopImmediatePropagation();
    feedback(event);
    openPicker(trigger, select);
}, true);

document.addEventListener('click', event => {
    const target = event.target.closest?.('button, a[href], [role="option"], [role="button"], input[type="checkbox"], input[type="radio"]');
    if (target && !target.disabled) feedback(event);
}, true);
compact.addEventListener('change', closePicker);
window.addEventListener('popstate', closePicker);
window.addEventListener('pagehide', () => { closePicker(); haptics?.destroy(); haptics = null; });
new MutationObserver(() => {
    if (activePicker && !activePicker.trigger.isConnected) closePicker();
}).observe(document.body, { childList: true, subtree: true });
