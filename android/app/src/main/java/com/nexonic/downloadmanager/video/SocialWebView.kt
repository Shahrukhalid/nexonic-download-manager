package com.nexonic.downloadmanager.video

import android.annotation.SuppressLint
import android.content.Context
import android.view.View
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
 * the video links it shows are read with the site's script. The sites show visitors only their latest posts.
 */
object SocialWebView {
    @SuppressLint("SetJavaScriptEnabled")
    private fun webView(context: Context) = WebView(context).apply {
        settings.javaScriptEnabled = true
        settings.domStorageEnabled = true
        webViewClient = WebViewClient()
    }

    private suspend fun run(view: WebView, script: String) =
        suspendCancellableCoroutine<String?> { c -> view.evaluateJavascript(script) { if (c.isActive) c.resume(it) } }

    /** The videos the profile's pages show visitors; found(count) as they come; stopped() ends early. */
    suspend fun scan(context: Context, site: SocialSite, pages: List<String>, max: Int, found: (Int) -> Unit, stopped: () -> Boolean): Pair<Int, List<BatchEntry>> =
        withContext(Dispatchers.Main) {
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
                view.destroy()
            }
        }
}
