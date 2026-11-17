/**
 * Makes the page behave like a native panel inside the Rhino WebView: no browser context menu,
 * no zooming with Ctrl/Cmd + wheel or + / -, and no navigation when a file is dropped outside a
 * drop zone. Returns a function that removes the guards (used by tests).
 */
export function installPageGuards(target: Window = window, allowContextMenu = false): () => void {
  const onContextMenu = (event: Event) => {
    const element = event.target as HTMLElement | null;
    const editable = element?.closest('input, textarea, [contenteditable="true"]');
    if (!allowContextMenu && !editable) {
      event.preventDefault();
    }
  };
  const onWheel = (event: WheelEvent) => {
    if (event.ctrlKey || event.metaKey) {
      event.preventDefault();
    }
  };
  const onKeyDown = (event: KeyboardEvent) => {
    if ((event.ctrlKey || event.metaKey) && ['+', '-', '=', '0'].includes(event.key)) {
      event.preventDefault();
    }
  };
  const onDrop = (event: DragEvent) => event.preventDefault();

  target.addEventListener('contextmenu', onContextMenu);
  target.addEventListener('wheel', onWheel, { passive: false });
  target.addEventListener('keydown', onKeyDown);
  target.addEventListener('dragover', onDrop);
  target.addEventListener('drop', onDrop);
  return () => {
    target.removeEventListener('contextmenu', onContextMenu);
    target.removeEventListener('wheel', onWheel);
    target.removeEventListener('keydown', onKeyDown);
    target.removeEventListener('dragover', onDrop);
    target.removeEventListener('drop', onDrop);
  };
}
