// Web Push service worker for the hosted terminal (registered by js/webPush.js
// when the user enables browser notifications in Settings). Shows each pushed
// alert as an OS notification — screen readers announce these natively — and
// focuses/opens the terminal when the notification is activated.
self.addEventListener('push', (event) => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; } catch { /* non-JSON payload */ }
    const title = data.title || 'Accessible Trader';
    const body = data.body || 'Alert fired.';
    event.waitUntil(self.registration.showNotification(title, {
        body: body,
        // One tag per body keeps repeat fires of the SAME alert from stacking
        // an unbounded pile of identical notifications.
        tag: 'att-' + body,
    }));
});

self.addEventListener('notificationclick', (event) => {
    event.notification.close();
    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windows) => {
            for (const client of windows) {
                if ('focus' in client) return client.focus();
            }
            return clients.openWindow('./');
        })
    );
});
