package com.nexonic.downloadmanager.data

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import com.nexonic.downloadmanager.core.Net
import java.io.IOException
import java.net.Inet4Address

/**
 * "VPN only": every download, video and torrent goes through the VPN network, bound to it, so nothing uses another
 * network when the VPN disconnects (kill switch).
 */
object Vpn {
    const val NOT_CONNECTED = "No VPN is connected. Downloads wait for it instead of using another connection."
    private lateinit var app: Context

    @Volatile var required = false
        private set

    fun init(context: Context, on: Boolean) { app = context.applicationContext; set(on) }

    fun set(on: Boolean) {
        required = on
        // Regular downloads open their connections on the VPN network itself.
        Net.opener = if (on) { url -> (network() ?: throw IOException(NOT_CONNECTED)).openConnection(url) } else null
    }

    /** The connected VPN network, or null. */
    @Suppress("DEPRECATION")
    fun network(): Network? {
        val cm = app.getSystemService(ConnectivityManager::class.java) ?: return null
        return cm.allNetworks.firstOrNull { n ->
            cm.getNetworkCapabilities(n)?.let { it.hasTransport(NetworkCapabilities.TRANSPORT_VPN) && it.hasCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET) } == true
        }
    }

    /** True when downloads may run now (VPN not required, or connected). */
    fun ready(): Boolean = !required || network() != null

    /** The VPN's own address, which torrents and the video engine bind to; null when VPN only is off. */
    fun sourceAddress(): String? {
        if (!required) return null
        val net = network() ?: throw IOException(NOT_CONNECTED)
        val cm = app.getSystemService(ConnectivityManager::class.java) ?: throw IOException(NOT_CONNECTED)
        val addresses = cm.getLinkProperties(net)?.linkAddresses?.map { it.address }.orEmpty().filter { !it.isLinkLocalAddress }
        return (addresses.firstOrNull { it is Inet4Address } ?: addresses.firstOrNull())?.hostAddress?.substringBefore('%')
            ?: throw IOException(NOT_CONNECTED)
    }
}
