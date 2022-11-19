

extern "C" void func_throws();

extern "C" void func_with_eh() {
    try {
        func_throws();
    }
    catch (...) {

    }
}

