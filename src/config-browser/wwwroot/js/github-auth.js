window.githubAuth = {
    signIn: (url, expectedOrigin) => new Promise((resolve, reject) => {
        const popup = window.open(url, "github-app-login", "popup=yes,width=720,height=820");

        if (!popup) {
            reject("GitHub sign-in popup was blocked by the browser.");
            return;
        }

        let completed = false;
        const timer = window.setInterval(() => {
            if (!popup.closed) {
                return;
            }

            window.clearInterval(timer);

            if (!completed) {
                window.removeEventListener("message", onMessage);
                reject("GitHub sign-in was cancelled before it completed.");
            }
        }, 500);

        const onMessage = (event) => {
            if (event.origin !== expectedOrigin) {
                return;
            }

            if (event.data?.type !== "github-app-auth-complete") {
                return;
            }

            completed = true;
            window.clearInterval(timer);
            window.removeEventListener("message", onMessage);

            if (!popup.closed) {
                popup.close();
            }

            if (event.data.error) {
                reject(event.data.error);
                return;
            }

            resolve(event.data.authSession);
        };

        window.addEventListener("message", onMessage);
    })
};
