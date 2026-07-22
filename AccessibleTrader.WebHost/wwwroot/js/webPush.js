// Web Push enrolment for the hosted terminal. All URLs are RELATIVE so they
// resolve under the app's base href (/terminal/ on hosted). Invoked from the
// Settings modal via JS interop; every function returns a short status string
// the caller speaks, and never throws across the interop boundary.
(function () {
    'use strict';

    function urlBase64ToUint8Array(base64String) {
        const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
        const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
        const raw = atob(base64);
        const output = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; ++i) output[i] = raw.charCodeAt(i);
        return output;
    }

    window.accessibleTraderPush = {
        isSupported: function () {
            return 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
        },

        enable: async function () {
            try {
                if (!this.isSupported()) return 'unsupported';

                const permission = await Notification.requestPermission();
                if (permission !== 'granted') return 'denied';

                const registration = await navigator.serviceWorker.register('service-worker.js');
                await navigator.serviceWorker.ready;

                const keyResponse = await fetch('push/vapid-public-key');
                if (!keyResponse.ok) return 'error';
                const publicKey = (await keyResponse.text()).trim();

                const subscription = await registration.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: urlBase64ToUint8Array(publicKey),
                });

                const json = subscription.toJSON();
                const response = await fetch('push/subscribe', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        endpoint: subscription.endpoint,
                        p256dh: json.keys.p256dh,
                        auth: json.keys.auth,
                    }),
                });
                return response.ok ? 'enabled' : 'error';
            } catch (e) {
                console.warn('webPush.enable failed', e);
                return 'error';
            }
        },

        disable: async function () {
            try {
                if (!this.isSupported()) return 'unsupported';
                const registration = await navigator.serviceWorker.getRegistration('service-worker.js');
                const subscription = registration && await registration.pushManager.getSubscription();
                if (subscription) {
                    await fetch('push/unsubscribe', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ endpoint: subscription.endpoint, p256dh: 'x', auth: 'x' }),
                    });
                    await subscription.unsubscribe();
                }
                return 'disabled';
            } catch (e) {
                console.warn('webPush.disable failed', e);
                return 'error';
            }
        },

        status: async function () {
            try {
                if (!this.isSupported()) return 'unsupported';
                const registration = await navigator.serviceWorker.getRegistration('service-worker.js');
                const subscription = registration && await registration.pushManager.getSubscription();
                return subscription ? 'enabled' : 'disabled';
            } catch {
                return 'error';
            }
        },
    };
})();
