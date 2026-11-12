package com.nexonic.downloadmanager.video

import android.annotation.SuppressLint
import android.app.Dialog
import android.content.Context
import android.view.View
import android.view.ViewGroup
import android.webkit.CookieManager
import android.webkit.WebView
import android.webkit.WebViewClient
import com.nexonic.downloadmanager.core.BatchEntry
import com.nexonic.downloadmanager.core.InstagramPage
import com.nexonic.downloadmanager.core.SocialPages
import com.nexonic.downloadmanager.core.SocialSite
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlin.coroutines.resume

/**
 * Profile Batch for Instagram and Facebook: the profile page opens in a hidden web view, as in a visitor's browser, and
 * the video links it shows are read with the site's script. Without signing in the site shows only the latest posts.
 * After the optional sign-in (on the site's own page, inside Nexonic) the page is scrolled to the end.
 */
object SocialWebView {
    private fun prefs(context: Context) = context.getSharedPreferences("social-sign-in", Context.MODE_PRIVATE)
    fun signedIn(context: Context, site: SocialSite) = prefs(context).getBoolean(site.name, false)

    private fun session(site: SocialSite) =
        CookieManager.getInstance().getCookie("https://www.${site.domain}")?.split(';')?.any { it.trim().startsWith(site.sessionCookie + "=") } == true

    @SuppressLint("SetJavaScriptEnabled")
    private fun webView(context: Context) = WebView(context).apply {
        settings.javaScriptEnabled = true
        settings.domStorageEnabled = true
        webViewClient = WebViewClient()
    }

    private suspend fun run(view: WebView, script: String) =
        suspendCancellableCoroutine<String?> { c -> view.evaluateJavascript(script) { if (c.isActive) c.resume(it) } }

    /** Every video the profile's pages show (all of them when signed in); found(count) as they come; stopped() ends early. */
    suspend fun scan(context: Context, site: SocialSite, pages: List<String>, max: Int, found: (Int) -> Unit, stopped: () -> Boolean): Pair<Int, List<BatchEntry>> =
        withContext(Dispatchers.Main) {
            val signedIn = signedIn(context, site)
            val view = webView(context.applicationContext)
            // Laid out at a phone's size without being shown, so the page builds its grid as it would on screen.
            view.measure(View.MeasureSpec.makeMeasureSpec(1080, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(1920, View.MeasureSpec.EXACTLY))
            view.layout(0, 0, 1080, 1920)
            val all = LinkedHashMap<String, BatchEntry>()
            var posts = 0
            try {
                for (page in pages) {
                    view.loadUrl(page)
                    var idle = 0
                    for (i in 0 until if (signedIn) 2400 else 25) {
                        if (idle >= (if (signedIn) 5 else 2) || all.size >= max || stopped()) break
                        delay(if (signedIn) 1500 else 1000)
                        val now = InstagramPage.read(run(view, site.script))
                        posts = maxOf(posts, now.first)
                        val grew = SocialPages.merge(all, now.second, max)
                        idle = if (grew || (all.isEmpty() && i < 10)) 0 else idle + 1
                        if (grew) found(all.size)
                        if (signedIn) run(view, SocialPages.SCROLL)
                    }
                }
                posts to all.values.toList()
            } finally {
                view.stopLoading()
                view.destroy()
            }
        }

    /**
     * The site's own sign-in page in a window of Nexonic's: the password goes to the site, never to Nexonic. Returns true
     * once the site has signed the user in (its session cookie is there); the window then closes by itself.
     */
    suspend fun signIn(context: Context, site: SocialSite): Boolean = withContext(Dispatchers.Main) {
        val view = webView(context)
        val dialog = Dialog(context, android.R.style.Theme_DeviceDefault_NoActionBar)
        dialog.setContentView(view, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        dialog.show()
        view.loadUrl(site.loginUrl)
        var done = false
        try {
            while (dialog.isShowing) {
                delay(1500)
                if (session(site)) {
                    prefs(context).edit().putBoolean(site.name, true).apply()
                    done = true
                    dialog.dismiss()
                }
            }
        } finally {
            view.destroy()
        }
        done
    }

    /** Removes Nexonic's session for the site (the site's own account is untouched). */
    fun signOut(context: Context, site: SocialSite) {
        prefs(context).edit().putBoolean(site.name, false).apply()
        val cookies = CookieManager.getInstance()
        for (host in listOf("https://www.${site.domain}", "https://${site.domain}", "https://m.${site.domain}")) {
            cookies.getCookie(host)?.split(';')?.map { it.substringBefore('=').trim() }?.filter { it.isNotEmpty() }?.forEach { name ->
                cookies.setCookie(host, "$name=; Max-Age=0; Domain=.${site.domain}; Path=/")
            }
        }
        cookies.flush()
    }
}
