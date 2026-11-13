package com.nexonic.downloadmanager.video

import android.annotation.SuppressLint
import android.content.Context
import android.view.View
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.webkit.ProfileStore
import androidx.webkit.WebViewCompat
import androidx.webkit.WebViewFeature
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
 * the video links it shows are read with the site's script. The sites show visitors only their latest posts.
 *
 * The view runs in a web profile of its own, with its own cookies and storage, emptied after every scan: it never sees
 * the Browser tab's sign-in, and nothing it is given is kept. Where the system's WebView can't keep profiles apart
 * ([isolated] is false), Profile Batch doesn't open the page at all and uses the engine's cookie-free listing instead.
 */
object SocialWebView {
    private const val PROFILE = "nexonic-profile-batch"

    /** True when the hidden view can run without the browser's cookies (WebView with separate profiles). */
    fun isolated(): Boolean = runCatching { WebViewFeature.isFeatureSupported(WebViewFeature.MULTI_PROFILE) }.getOrDefault(false)

    @SuppressLint("SetJavaScriptEnabled")
    private fun webView(context: Context) = WebView(context).apply {
        // Before anything else touches the view: its profile can't change once it has loaded a page.
        WebViewCompat.setProfile(this, PROFILE)
        ProfileStore.getInstance().getOrCreateProfile(PROFILE).cookieManager.setAcceptThirdPartyCookies(this, false)
        settings.javaScriptEnabled = true
        settings.domStorageEnabled = true
        webViewClient = WebViewClient()
    }

    /** What the visitor's page left behind (cookies, storage, cache): gone, so the next scan starts as a new visitor. */
    private fun forget(view: WebView) {
        runCatching {
            val p = ProfileStore.getInstance().getOrCreateProfile(PROFILE)
            p.cookieManager.removeAllCookies(null)
            p.cookieManager.flush()
            p.webStorage.deleteAllData()
            view.clearCache(true)
        }
    }

    private suspend fun run(view: WebView, script: String) =
        suspendCancellableCoroutine<String?> { c -> view.evaluateJavascript(script) { if (c.isActive) c.resume(it) } }

    /**
     * The videos the profile's pages show visitors; found(count) as they come; stopped() ends early. Only call it when
     * [isolated] is true: it throws IllegalStateException otherwise, so a visitor's scan never uses the browser's sign-in.
     */
    suspend fun scan(context: Context, site: SocialSite, pages: List<String>, max: Int, found: (Int) -> Unit, stopped: () -> Boolean): Pair<Int, List<BatchEntry>> =
        withContext(Dispatchers.Main) {
            check(isolated()) { "This WebView can't keep Profile Batch apart from the browser's sign-in" }
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
                    for (i in 0 until 25) {
                        if (idle >= 2 || all.size >= max || stopped()) break
                        delay(1000)
                        val now = InstagramPage.read(run(view, site.script))
                        posts = maxOf(posts, now.first)
                        val grew = SocialPages.merge(all, now.second, max)
                        idle = if (grew || (all.isEmpty() && i < 10)) 0 else idle + 1
                        if (grew) found(all.size)
                    }
                }
                posts to all.values.toList()
            } finally {
                view.stopLoading()
                forget(view)
                view.destroy()
            }
        }
}
