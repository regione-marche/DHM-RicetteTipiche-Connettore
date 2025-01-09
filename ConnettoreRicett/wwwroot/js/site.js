$(window).scroll(function () {
    var headerNavWrapper = $("#header-nav-wrapper");
    var height = $(window).scrollTop()

    if (height > 70) {
        // compact menu
        headerNavWrapper.addClass("header-slim")
    } else {
        // full menu
         headerNavWrapper.removeClass("header-slim")
    }
})